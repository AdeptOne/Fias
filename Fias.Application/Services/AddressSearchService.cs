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
        string query, int limit, double threshold, CancellationToken ct)
    {
        var clean = (query ?? string.Empty).Trim();
        if (clean.Length < 2)
            return Array.Empty<AddressSearchResultDto>();

        limit = Math.Clamp(limit, 1, 100);
        threshold = Math.Clamp(threshold, 0.1, 1.0);

        var hits = await searchRepository.SearchByNameAsync(clean, limit, threshold, ct);
        logger.LogDebug("Поиск '{Query}' → {Count} результатов (порог {Threshold})", clean, hits.Count, threshold);

        var results = new List<AddressSearchResultDto>(hits.Count);
        foreach (var hit in hits)
        {
            var address = await builder.BuildByObjectIdAsync(hit.ObjectId, ct);
            var fullName = address?.Hierarchy[^1].FullName
                           ?? string.Join(" ", new[] { hit.TypeName, hit.Name }
                               .Where(s => !string.IsNullOrEmpty(s)));

            results.Add(new AddressSearchResultDto(
                hit.ObjectId,
                hit.ObjectGuid,
                hit.Level,
                hit.Name,
                fullName,
                address?.Address ?? fullName,
                hit.Similarity));
        }
        return results;
    }

    public async Task<IReadOnlyList<AddressChildDto>> GetChildrenAsync(long objectId, CancellationToken ct)
    {
        var childObjectIds = await db.AdmHierarchy.AsNoTracking()
            .Where(h => h.ParentObjId == objectId && h.IsActive == 1)
            .Select(h => h.ObjectId)
            .ToListAsync(ct);

        if (childObjectIds.Count == 0)
            return Array.Empty<AddressChildDto>();

        var result = new List<AddressChildDto>(childObjectIds.Count);
        foreach (var id in childObjectIds)
        {
            var addr = await builder.BuildByObjectIdAsync(id, ct);
            if (addr is null) continue;
            var leaf = addr.Hierarchy[^1];
            result.Add(new AddressChildDto(addr.ObjectId, addr.ObjectGuid, addr.Level, leaf.FullName));
        }
        return result;
    }
}
