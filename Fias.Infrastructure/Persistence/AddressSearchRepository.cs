using Fias.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Fias.Infrastructure.Persistence;

/// <summary>
/// pg_trgm-поиск. Использует EF.Functions.TrigramsSimilarity, который провайдер Npgsql
/// транслирует в similarity(text, text). Требует расширения pg_trgm и GIN-индекса по name.
/// </summary>
public class AddressSearchRepository(FiasDbContext db) : IAddressSearchRepository
{
    private const int HouseLevel = 10;

    public async Task<IReadOnlyList<AddressSearchHit>> SearchByNameAsync(
        string query,
        int limit,
        double threshold,
        int? levelFilter,
        IReadOnlyCollection<long>? restrictToObjectIds,
        CancellationToken ct)
    {
        var hits = new List<AddressSearchHit>(limit * 2);

        // Адресообразующие объекты (регион/город/улица/...) — поиск по NAME.
        if (levelFilter is null or not HouseLevel)
            hits.AddRange(await SearchAddressObjectsAsync(query, limit, threshold, levelFilter, restrictToObjectIds, ct));

        // Дома (уровень 10) — поиск по номеру дома (HOUSENUM).
        if (levelFilter is null or HouseLevel)
            hits.AddRange(await SearchHousesAsync(query, limit, threshold, restrictToObjectIds, ct));

        return hits
            .OrderByDescending(h => h.Similarity)
            .Take(limit)
            .ToList();
    }

    private async Task<List<AddressSearchHit>> SearchAddressObjectsAsync(
        string query, int limit, double threshold, int? levelFilter,
        IReadOnlyCollection<long>? restrictToObjectIds, CancellationToken ct)
    {
        var q = db.AddressObjects.AsNoTracking()
            .Where(a => a.IsActual == true && a.IsActive == true && a.Name != null);

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

    private async Task<List<AddressSearchHit>> SearchHousesAsync(
        string query, int limit, double threshold,
        IReadOnlyCollection<long>? restrictToObjectIds, CancellationToken ct)
    {
        var q = db.Houses.AsNoTracking()
            .Where(h => h.IsActual == true && h.IsActive == true && h.HouseNum != null);

        if (restrictToObjectIds is { Count: > 0 } ids)
            q = q.Where(h => ids.Contains(h.ObjectId));

        var rows = await q
            .Select(h => new
            {
                h.ObjectId,
                h.ObjectGuid,
                h.HouseNum,
                Similarity = EF.Functions.TrigramsSimilarity(h.HouseNum!, query)
            })
            .Where(x => x.Similarity >= threshold)
            .OrderByDescending(x => x.Similarity)
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(r => new AddressSearchHit(r.ObjectId, r.ObjectGuid, HouseLevel, r.HouseNum, null, r.Similarity))
            .ToList();
    }
}
