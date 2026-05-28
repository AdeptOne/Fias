using Fias.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Fias.Infrastructure.Persistence;

/// <summary>
/// pg_trgm-поиск. Использует EF.Functions.TrigramsSimilarity, который провайдер Npgsql
/// транслирует в similarity(text, text). Требует расширения pg_trgm и GIN-индекса по name.
/// </summary>
public class AddressSearchRepository(FiasDbContext db) : IAddressSearchRepository
{
    public async Task<IReadOnlyList<AddressSearchHit>> SearchByNameAsync(
        string query,
        int limit,
        double threshold,
        int? levelFilter,
        IReadOnlyCollection<long>? restrictToObjectIds,
        CancellationToken ct)
    {
        var q = db.AddressObjects.AsNoTracking()
            .Where(a => a.IsActual == 1 && a.IsActive == 1 && a.Name != null);

        if (levelFilter is { } lvl)
            q = q.Where(a => a.Level == lvl);

        if (restrictToObjectIds is { Count: > 0 } ids)
            q = q.Where(a => ids.Contains(a.ObjectId));

        var rows = await q
            .Select(a => new
            {
                a.ObjectId,
                a.ObjectGuid,
                a.Level,
                a.Name,
                a.TypeName,
                Similarity = EF.Functions.TrigramsSimilarity(a.Name!, query)
            })
            .Where(x => x.Similarity >= threshold)
            .OrderByDescending(x => x.Similarity)
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(r => new AddressSearchHit(r.ObjectId, r.ObjectGuid, r.Level, r.Name, r.TypeName, r.Similarity))
            .ToList();
    }
}
