using Fias.Application.Abstractions;
using Fias.Application.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fias.Application.Services;

public class AddressSearchService(
    IFiasDbContext db,
    IAddressSearchRepository searchRepository,
    IAddressBuilderService builder,
    ILogger<AddressSearchService> logger) : IAddressSearchService
{
    private const int HouseLevel = 10;

    // Служебные слова номера дома — их отбрасываем из «именной» части перед поиском улицы.
    private static readonly HashSet<string> HouseKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "д", "д.", "дом", "к", "к.", "корп", "корп.", "корпус", "стр", "стр.", "строение",
        "влд", "влд.", "владение", "зд", "зд.", "здание", "лит", "литера", "соор", "сооружение"
    };

    private static readonly char[] QuerySeparators = [' ', ',', ';'];

    public async Task<IReadOnlyList<AddressSearchResultDto>> SearchAsync(
        string query, int limit, double threshold, int? level, long? parentId, CancellationToken ct)
    {
        var clean = (query ?? string.Empty).Trim();
        if (clean.Length < 2)
            return Array.Empty<AddressSearchResultDto>();

        limit = Math.Clamp(limit, 1, 100);
        threshold = Math.Clamp(threshold, 0.1, 1.0);

        // Поиск по FIAS GUID: если запрос — это GUID, резолвим объект напрямую (по всем типам).
        if (Guid.TryParse(clean, out var guid))
        {
            var byGuidId = await ResolveObjectIdByGuidAsync(guid, ct);
            if (byGuidId is null) return Array.Empty<AddressSearchResultDto>();
            return [await MakeResultAsync(byGuidId.Value, guid, null, null, string.Empty, 1.0, ct)];
        }

        // Если задан parentId — ограничиваем поиск его поддеревом через PATH LIKE.
        IReadOnlyCollection<long>? restrict = null;
        if (parentId is { } pid)
        {
            var parentPath = await db.AdmHierarchy.AsNoTracking()
                .Where(h => h.ObjectId == pid && h.IsActive == true)
                .Select(h => h.Path)
                .FirstOrDefaultAsync(ct);
            if (parentPath is null) return Array.Empty<AddressSearchResultDto>();

            var prefix = parentPath + ".";
            restrict = await db.AdmHierarchy.AsNoTracking()
                .Where(h => h.IsActive == true && h.Path != null && EF.Functions.Like(h.Path!, prefix + "%"))
                .Select(h => h.ObjectId)
                .ToListAsync(ct);
        }

        // Многоуровневый разбор: «Челябинская область Челябинск Ленина 5» → регион → город →
        // улица → дом. Если разбор дал результат — возвращаем его, иначе обычный нечёткий поиск.
        if (level is null or HouseLevel)
        {
            var drilled = await TryDrillDownAsync(clean, restrict, limit, threshold, ct);
            if (drilled is not null)
            {
                logger.LogDebug("Разбор адреса '{Query}' → {Count} результатов", clean, drilled.Count);
                return drilled;
            }
        }

        var hits = await searchRepository.SearchByNameAsync(clean, limit, threshold, level, restrict, ct);
        logger.LogDebug("Поиск '{Query}' → {Count} результатов (порог {Threshold})", clean, hits.Count, threshold);

        var results = new List<AddressSearchResultDto>(hits.Count);
        foreach (var hit in hits)
        {
            var fallback = string.Join(" ", new[] { hit.TypeName?.Trim(), hit.Name?.Trim() }
                .Where(s => !string.IsNullOrEmpty(s)));
            results.Add(await MakeResultAsync(
                hit.ObjectId, hit.ObjectGuid, hit.Level, hit.Name, fallback, hit.Similarity, ct));
        }
        return results;
    }

    /// <summary>
    /// Многоуровневый разбор адресной строки. Отделяет номер дома, затем по «именной» части
    /// проходом слева-направо сужает контекст (регион → район → город → улица), сопоставляя
    /// каждый сегмент через pg_trgm в поддереве уже найденного. Возвращает:
    /// дома с искомым номером в поддереве; либо найденный объект (если номера нет, а сегментов ≥2);
    /// либо null — «разбор не применим», вызывающий код делает обычный нечёткий поиск.
    /// </summary>
    private async Task<IReadOnlyList<AddressSearchResultDto>?> TryDrillDownAsync(
        string query, IReadOnlyCollection<long>? restrict, int limit, double threshold, CancellationToken ct)
    {
        var (namePart, houseNum) = SplitTrailingHouse(query);

        if (houseNum is not null)
        {
            IReadOnlyCollection<long>? scope;
            double baseSim;
            if (namePart.Length >= 2)
            {
                var ctx = await ResolveContextAsync(namePart, restrict, threshold, ct);
                if (ctx.ObjectId is null) return null;        // улицу/нас. пункт не нашли — фолбэк
                scope = ctx.Subtree;
                baseSim = ctx.Sim;
            }
            else
            {
                scope = restrict;                             // напр. «д 5» с заданным parentId
                baseSim = 1.0;
            }

            if (scope is not { Count: > 0 }) return null;
            var houses = await MatchHousesInScopeAsync(scope, houseNum, baseSim, limit, ct);
            return houses.Count > 0 ? houses : null;
        }

        // Номера дома нет: многоуровневый разбор имеет смысл только для многословной строки.
        if (!namePart.Contains(' ')) return null;

        var resolved = await ResolveContextAsync(namePart, restrict, threshold, ct);
        if (resolved.ObjectId is null || resolved.Segments < 2) return null;
        return [await MakeResultAsync(resolved.ObjectId.Value, null, resolved.Level, null, string.Empty, resolved.Sim, ct)];
    }

    private readonly record struct ResolvedContext(
        long? ObjectId, int? Level, double Sim, IReadOnlyCollection<long>? Subtree, int Segments);

    /// <summary>
    /// Жадный проход по токенам именной части. В каждой позиции пробует фразы длиной 1..3 токена
    /// и выбирает кандидата (не дом) с максимальной similarity в текущем поддереве; затем сужает
    /// контекст до его поддерева и продолжает с остатка. Нераспознанные токены (типы «город», «ул»)
    /// просто пропускаются.
    /// </summary>
    private async Task<ResolvedContext> ResolveContextAsync(
        string namePart, IReadOnlyCollection<long>? restrict, double threshold, CancellationToken ct)
    {
        var tokens = namePart.Split(QuerySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return new ResolvedContext(null, null, 0, restrict, 0);

        const int maxPhrase = 3;
        const int maxTokens = 8;

        var context = restrict;
        long? matchedId = null;
        int? matchedLevel = null;
        double matchedSim = 0;
        var segments = 0;

        var i = 0;
        var processed = 0;
        while (i < tokens.Length && processed < maxTokens)
        {
            AddressSearchHit? best = null;
            var bestLen = 0;
            var maxLen = Math.Min(maxPhrase, tokens.Length - i);
            for (var len = 1; len <= maxLen; len++)
            {
                var phrase = string.Join(' ', tokens, i, len);
                if (phrase.Length < 2) continue;

                var hits = await searchRepository.SearchByNameAsync(phrase, 5, threshold, null, context, ct);
                var cand = hits.Where(h => h.Level != HouseLevel)
                    .OrderByDescending(h => h.Similarity).FirstOrDefault();
                // Более длинная фраза побеждает при сопоставимой similarity (>=), чтобы
                // «Карла Маркса» бралось целиком, а не как «Карла».
                if (cand is not null && (best is null || cand.Similarity >= best.Similarity))
                {
                    best = cand;
                    bestLen = len;
                }
            }

            if (best is null) { i++; processed++; continue; }

            matchedId = best.ObjectId;
            matchedLevel = best.Level;
            matchedSim = best.Similarity;
            segments++;
            context = await GetSubtreeAsync(best.ObjectId, ct);
            i += bestLen;
            processed += bestLen;
        }

        return new ResolvedContext(matchedId, matchedLevel, matchedSim, context, segments);
    }

    /// <summary>Все object_id поддерева объекта (включая сам объект) по PATH в adm_hierarchy.</summary>
    private async Task<IReadOnlyCollection<long>?> GetSubtreeAsync(long objectId, CancellationToken ct)
    {
        var path = await db.AdmHierarchy.AsNoTracking()
            .Where(h => h.ObjectId == objectId && h.IsActive == true)
            .Select(h => h.Path)
            .FirstOrDefaultAsync(ct);
        if (path is null) return new[] { objectId };

        var prefix = path + ".";
        var ids = await db.AdmHierarchy.AsNoTracking()
            .Where(h => h.IsActive == true && h.Path != null && EF.Functions.Like(h.Path!, prefix + "%"))
            .Select(h => h.ObjectId)
            .ToListAsync(ct);
        ids.Add(objectId);
        return ids;
    }

    /// <summary>Сопоставляет дома по нормализованному номеру среди object_id заданного поддерева.</summary>
    private async Task<IReadOnlyList<AddressSearchResultDto>> MatchHousesInScopeAsync(
        IReadOnlyCollection<long> scope, string houseNum, double baseSim, int limit, CancellationToken ct)
    {
        var wanted = NormalizeHouse(houseNum);
        if (wanted.Length == 0) return Array.Empty<AddressSearchResultDto>();

        var scopeIds = scope as IReadOnlyList<long> ?? scope.ToList();
        var houses = await db.Houses.AsNoTracking()
            .Where(x => x.IsActual == true && x.IsActive == true
                        && x.HouseNum != null && scopeIds.Contains(x.ObjectId))
            .Select(x => new { x.ObjectId, x.ObjectGuid, x.HouseNum })
            .ToListAsync(ct);

        var matched = new List<(long ObjectId, Guid? Guid, string? Name, double Sim)>();
        foreach (var h in houses)
        {
            var norm = NormalizeHouse(h.HouseNum!);
            if (norm.Length == 0) continue;

            double factor;
            if (norm == wanted) factor = 1.0;
            else if (norm.StartsWith(wanted, StringComparison.Ordinal)) factor = 0.85;
            else continue;

            matched.Add((h.ObjectId, h.ObjectGuid, h.HouseNum?.Trim(), baseSim * factor));
        }

        var top = matched.OrderByDescending(m => m.Sim).Take(limit).ToList();
        var results = new List<AddressSearchResultDto>(top.Count);
        foreach (var m in top)
            results.Add(await MakeResultAsync(m.ObjectId, m.Guid, HouseLevel, m.Name, m.Name ?? string.Empty, m.Sim, ct));
        return results;
    }

    /// <summary>Резолвит OBJECTID по OBJECTGUID, перебирая типы объектов (адрес/дом/квартира/комната).</summary>
    private async Task<long?> ResolveObjectIdByGuidAsync(Guid guid, CancellationToken ct)
    {
        var addr = await db.AddressObjects.AsNoTracking()
            .Where(a => a.ObjectGuid == guid && a.IsActual == true && a.IsActive == true)
            .Select(a => (long?)a.ObjectId).FirstOrDefaultAsync(ct);
        if (addr is not null) return addr;

        var house = await db.Houses.AsNoTracking()
            .Where(h => h.ObjectGuid == guid && h.IsActual == true && h.IsActive == true)
            .Select(h => (long?)h.ObjectId).FirstOrDefaultAsync(ct);
        if (house is not null) return house;

        var apt = await db.Apartments.AsNoTracking()
            .Where(a => a.ObjectGuid == guid && a.IsActual == true && a.IsActive == true)
            .Select(a => (long?)a.ObjectId).FirstOrDefaultAsync(ct);
        if (apt is not null) return apt;

        var room = await db.Rooms.AsNoTracking()
            .Where(r => r.ObjectGuid == guid && r.IsActual == true && r.IsActive == true)
            .Select(r => (long?)r.ObjectId).FirstOrDefaultAsync(ct);
        return room;
    }

    private async Task<AddressSearchResultDto> MakeResultAsync(
        long objectId, Guid? objectGuid, int? level, string? name, string fallbackFull, double similarity, CancellationToken ct)
    {
        var address = await builder.BuildByObjectIdAsync(objectId, ct);
        var fullName = address?.Hierarchy.Count > 0 ? address.Hierarchy[^1].FullName : fallbackFull;
        return new AddressSearchResultDto(
            objectId, objectGuid, level, name?.Trim(), fullName, address?.FullName ?? fullName, similarity);
    }

    /// <summary>
    /// Делит строку на именную часть и номер дома. Номером считается последний токен, начинающийся
    /// с цифры («5», «12А», «3/1»). Хвостовые служебные слова («дом», «корп» и т.п.) отбрасываются
    /// из именной части. Если номера нет — houseNum = null.
    /// </summary>
    private static (string NamePart, string? HouseNum) SplitTrailingHouse(string query)
    {
        var tokens = query.Split(QuerySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2) return (query, null);

        var last = tokens[^1];
        if (!char.IsDigit(last[0])) return (query, null);

        var nameTokens = tokens[..^1].ToList();
        while (nameTokens.Count > 0 && HouseKeywords.Contains(nameTokens[^1]))
            nameTokens.RemoveAt(nameTokens.Count - 1);

        return (string.Join(' ', nameTokens), last);
    }

    private static string NormalizeHouse(string value)
    {
        Span<char> buffer = value.Length <= 64 ? stackalloc char[value.Length] : new char[value.Length];
        var n = 0;
        foreach (var ch in value)
        {
            if (ch is ' ' or '.' or '\t') continue;
            var c = char.ToLowerInvariant(ch);
            buffer[n++] = c == 'ё' ? 'е' : c;
        }
        return new string(buffer[..n]);
    }

    public async Task<AddressListResponse> SearchAddressesAsync(
        string query, int limit, double threshold, int? level, long? parentId, CancellationToken ct)
    {
        var hits = await SearchAsync(query, limit, threshold, level, parentId, ct);

        var addresses = new List<AddressDto>(hits.Count);
        foreach (var hit in hits)
        {
            var address = await builder.BuildByObjectIdAsync(hit.ObjectId, ct);
            if (address is not null) addresses.Add(address);
        }
        return new AddressListResponse(addresses);
    }

    public async Task<IReadOnlyList<AddressChildDto>> GetChildrenAsync(
        long objectId, int? level, string? nameFilter, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);

        // Список дочерних OBJECTID. При фильтре по уровню обогащаем join'ом с reestr_objects.
        var query = db.AdmHierarchy.AsNoTracking()
            .Where(h => h.ParentObjId == objectId && h.IsActive == true);

        var childObjectIds = await query.Select(h => h.ObjectId).ToListAsync(ct);
        if (childObjectIds.Count == 0)
            return Array.Empty<AddressChildDto>();

        // Фильтр по уровню — через reestr_objects.
        if (level is { } lvl)
        {
            var filtered = await db.ReestrObjects.AsNoTracking()
                .Where(r => childObjectIds.Contains(r.ObjectId) && r.LevelId == lvl)
                .Select(r => r.ObjectId)
                .ToListAsync(ct);
            childObjectIds = filtered;
        }

        // Фильтр по имени — по addressobjects.name. Используем ToLower().Contains(),
        // EF Core транслирует в lower(name) LIKE '%pattern%' (провайдер-агностично).
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var pattern = nameFilter.Trim().ToLowerInvariant();
            var filtered = await db.AddressObjects.AsNoTracking()
                .Where(a => childObjectIds.Contains(a.ObjectId)
                            && a.IsActual == true && a.IsActive == true
                            && a.Name != null
                            && a.Name!.ToLower().Contains(pattern))
                .Select(a => a.ObjectId)
                .ToListAsync(ct);
            childObjectIds = filtered;
        }

        var pageItems = childObjectIds.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var result = new List<AddressChildDto>(pageItems.Count);
        foreach (var id in pageItems)
        {
            var addr = await builder.BuildByObjectIdAsync(id, ct);
            if (addr is null) continue;
            var leaf = addr.Hierarchy[^1];
            result.Add(new AddressChildDto(addr.ObjectId, addr.ObjectGuid, addr.ObjectLevelId, leaf.FullName));
        }
        return result;
    }

    public async Task<IReadOnlyList<AddressHierarchyItemDto>> GetParentsAsync(long objectId, CancellationToken ct)
    {
        var address = await builder.BuildByObjectIdAsync(objectId, ct);
        return address?.Hierarchy ?? Array.Empty<AddressHierarchyItemDto>();
    }

}
