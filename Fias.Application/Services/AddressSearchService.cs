using Dapper;
using Fias.Application.Abstractions;
using Fias.Application.Models;
using Fias.Application.Search;
using Microsoft.Extensions.Logging;

namespace Fias.Application.Services;

/// <summary>
/// Поиск адресов. Свободный текст нормализуется (<see cref="AddressNormalizer"/>) и уходит в
/// гибридный репозиторий (FTS + триграммы + поуровневое сужение). Построение полной адресной
/// строки и навигация по дереву (children/parents) идут сырым SQL через <see cref="ISqlConnectionFactory"/>.
/// </summary>
public class AddressSearchService(
    ISqlConnectionFactory factory,
    IAddressSearchRepository searchRepository,
    IAddressBuilderService builder,
    AddressNormalizer normalizer,
    ILogger<AddressSearchService> logger) : IAddressSearchService
{
    public async Task<IReadOnlyList<AddressSearchResultDto>> SearchAsync(
        string query, int limit, double threshold, int? level, long? parentId, CancellationToken ct)
    {
        var clean = (query ?? string.Empty).Trim();
        if (clean.Length < 2)
            return Array.Empty<AddressSearchResultDto>();

        limit = Math.Clamp(limit, 1, 100);
        threshold = Math.Clamp(threshold, 0.1, 1.0);

        // Запрос-GUID резолвим напрямую по всем типам объектов (минуя нечёткий поиск).
        if (Guid.TryParse(clean, out var guid))
        {
            var byGuidId = await ResolveObjectIdByGuidAsync(guid, ct);
            if (byGuidId is null) return Array.Empty<AddressSearchResultDto>();
            return [await MakeResultAsync(byGuidId.Value, guid, null, null, string.Empty, 1.0, ct)];
        }

        // Препроцессинг строки + параметры запроса.
        var parsed = normalizer.Parse(clean) with
        {
            Limit = limit,
            SimilarityThreshold = threshold,
            LevelFilter = level,
            ParentObjectId = parentId,
        };

        var hits = await searchRepository.SearchAsync(parsed, ct);
        logger.LogDebug(
            "Поиск '{Query}' → {Count} рез. (город={City}, улица={Street}, дом={House})",
            clean, hits.Count, parsed.RegionOrCity, parsed.Street, parsed.House);

        const int houseLevel = 10;
        var results = new List<AddressSearchResultDto>(hits.Count);
        foreach (var hit in hits)
        {
            // full_name и реквизиты уже денормализованы в проекции search.* — билдер не нужен.
            var fallback = string.Join(" ", new[] { hit.TypeName?.Trim(), hit.Name?.Trim() }
                .Where(s => !string.IsNullOrEmpty(s)));
            var full = string.IsNullOrWhiteSpace(hit.FullName) ? fallback : hit.FullName!;

            var data = new AddressDataDto(
                FiasId: hit.ObjectGuid,
                FiasLevel: hit.Level,
                RegionCode: hit.RegionCode,
                Region: hit.Region,
                Area: hit.Area,
                City: hit.City,
                Settlement: hit.Settlement,
                Street: hit.Street,
                House: hit.Level == houseLevel ? hit.Name?.Trim() : null,
                PostalCode: hit.PostalCode,
                KladrId: hit.KladrCode,
                Okato: hit.Okato,
                Oktmo: hit.Oktmo,
                TaxOffice: hit.IfnsFl,
                TaxOfficeLegal: hit.IfnsUl);

            results.Add(new AddressSearchResultDto(
                hit.ObjectId, hit.ObjectGuid, hit.Level, hit.Name?.Trim(), full, full, hit.Score, data));
        }
        return results;
    }

    /// <summary>Резолвит OBJECTID по OBJECTGUID среди всех типов объектов (адрес/дом/квартира/комната).</summary>
    private async Task<long?> ResolveObjectIdByGuidAsync(Guid guid, CancellationToken ct)
    {
        const string sql = """
            SELECT objectid FROM fias.addressobjects WHERE objectguid = @guid AND isactual = true AND isactive = true
            UNION ALL
            SELECT objectid FROM fias.houses        WHERE objectguid = @guid AND isactual = true AND isactive = true
            UNION ALL
            SELECT objectid FROM fias.apartments    WHERE objectguid = @guid AND isactual = true AND isactive = true
            UNION ALL
            SELECT objectid FROM fias.rooms         WHERE objectguid = @guid AND isactual = true AND isactive = true
            LIMIT 1
            """;
        await using var conn = await factory.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<long?>(new CommandDefinition(sql, new { guid }, cancellationToken: ct));
    }

    private async Task<AddressSearchResultDto> MakeResultAsync(
        long objectId, Guid? objectGuid, int? level, string? name, string fallbackFull, double similarity, CancellationToken ct)
    {
        var address = await builder.BuildByObjectIdAsync(objectId, ct);
        var fullName = address?.Hierarchy.Count > 0 ? address.Hierarchy[^1].FullName : fallbackFull;
        return new AddressSearchResultDto(
            objectId, objectGuid, level, name?.Trim(), fullName, address?.FullName ?? fullName, similarity);
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

        // Дочерние OBJECTID одним запросом: JOIN с reestr_objects (фильтр уровня) и
        // addressobjects (фильтр имени) при необходимости; пагинация прямо в SQL.
        var sql = new System.Text.StringBuilder();
        sql.AppendLine("SELECT h.objectid FROM fias.adm_hierarchy h");
        if (level is not null)
            sql.AppendLine("JOIN fias.reestr_objects r ON r.objectid = h.objectid AND r.levelid = @level AND r.isactive = true");
        if (!string.IsNullOrWhiteSpace(nameFilter))
            sql.AppendLine("JOIN fias.addressobjects a ON a.objectid = h.objectid AND a.isactual = true AND a.isactive = true AND a.name ILIKE @pattern");
        sql.AppendLine("WHERE h.parentobjid = @objectId AND h.isactive = true");
        sql.AppendLine("ORDER BY h.objectid LIMIT @take OFFSET @skip");

        await using var conn = await factory.OpenAsync(ct);
        var pageItems = (await conn.QueryAsync<long>(new CommandDefinition(
            sql.ToString(),
            new
            {
                objectId,
                level,
                pattern = string.IsNullOrWhiteSpace(nameFilter) ? null : $"%{nameFilter.Trim()}%",
                take = pageSize,
                skip = (page - 1) * pageSize,
            },
            cancellationToken: ct))).AsList();

        if (pageItems.Count == 0)
            return Array.Empty<AddressChildDto>();

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
