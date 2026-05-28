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
    public async Task<IReadOnlyList<AddressSearchResultDto>> SearchAsync(
        string query, int limit, double threshold, int? level, long? parentId, CancellationToken ct)
    {
        var clean = (query ?? string.Empty).Trim();
        if (clean.Length < 2)
            return Array.Empty<AddressSearchResultDto>();

        limit = Math.Clamp(limit, 1, 100);
        threshold = Math.Clamp(threshold, 0.1, 1.0);

        // Если задан parentId — ограничиваем поиск его поддеревом через PATH LIKE.
        IReadOnlyCollection<long>? restrict = null;
        if (parentId is { } pid)
        {
            var parentPath = await db.AdmHierarchy.AsNoTracking()
                .Where(h => h.ObjectId == pid && h.IsActive == 1)
                .Select(h => h.Path)
                .FirstOrDefaultAsync(ct);
            if (parentPath is null) return Array.Empty<AddressSearchResultDto>();

            var prefix = parentPath + ".";
            restrict = await db.AdmHierarchy.AsNoTracking()
                .Where(h => h.IsActive == 1 && h.Path != null && EF.Functions.Like(h.Path!, prefix + "%"))
                .Select(h => h.ObjectId)
                .ToListAsync(ct);
        }

        var hits = await searchRepository.SearchByNameAsync(clean, limit, threshold, level, restrict, ct);
        logger.LogDebug("Поиск '{Query}' → {Count} результатов (порог {Threshold})", clean, hits.Count, threshold);

        var results = new List<AddressSearchResultDto>(hits.Count);
        foreach (var hit in hits)
        {
            var address = await builder.BuildByObjectIdAsync(hit.ObjectId, ct);
            var fullName = address?.Hierarchy[^1].FullName
                           ?? string.Join(" ", new[] { hit.TypeName?.Trim(), hit.Name?.Trim() }
                               .Where(s => !string.IsNullOrEmpty(s)));

            results.Add(new AddressSearchResultDto(
                hit.ObjectId,
                hit.ObjectGuid,
                hit.Level,
                hit.Name?.Trim(),
                fullName,
                address?.Address ?? fullName,
                hit.Similarity));
        }
        return results;
    }

    public async Task<IReadOnlyList<AddressChildDto>> GetChildrenAsync(
        long objectId, int? level, string? nameFilter, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);

        // Список дочерних OBJECTID. При фильтре по уровню обогащаем join'ом с reestr_objects.
        var query = db.AdmHierarchy.AsNoTracking()
            .Where(h => h.ParentObjId == objectId && h.IsActive == 1);

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
                            && a.IsActual == 1 && a.IsActive == 1
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
            result.Add(new AddressChildDto(addr.ObjectId, addr.ObjectGuid, addr.Level, leaf.FullName));
        }
        return result;
    }

    public async Task<IReadOnlyList<AddressHierarchyItemDto>> GetParentsAsync(long objectId, CancellationToken ct)
    {
        var address = await builder.BuildByObjectIdAsync(objectId, ct);
        return address?.Hierarchy ?? Array.Empty<AddressHierarchyItemDto>();
    }

}
