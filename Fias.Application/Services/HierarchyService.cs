using Fias.Application.Abstractions;
using Fias.Application.Models;
using Fias.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fias.Application.Services;

public class HierarchyService(IFiasDbContext db) : IHierarchyService
{
    private const int LevelRegion = 1;
    private const int LevelHouse = 10;
    private const int LevelApartment = 11;
    private const int LevelRoom = 12;

    public async Task<IReadOnlyList<RegionDto>> GetRegionsAsync(CancellationToken ct)
    {
        var regionReestrIds = await db.ReestrObjects.AsNoTracking()
            .Where(r => r.LevelId == LevelRegion && r.IsActive == true)
            .Select(r => r.ObjectId)
            .ToListAsync(ct);

        var regions = await db.AddressObjects.AsNoTracking()
            .Where(a => a.Level == LevelRegion && a.IsActual == true && a.IsActive == true
                        && regionReestrIds.Contains(a.ObjectId))
            .Select(a => new { a.ObjectId, a.ObjectGuid, a.Name, a.TypeName })
            .ToListAsync(ct);

        // Учитываем «официальное наименование» — для субъектов оно может перекрывать NAME.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var officials = await db.Params.AsNoTracking()
            .Where(p => regionReestrIds.Contains(p.ObjectId)
                        && p.TypeId == 16
                        && (p.EndDate == null || p.EndDate > today))
            .OrderBy(p => p.ObjectId).ThenByDescending(p => p.StartDate)
            .Select(p => new { p.ObjectId, p.Value })
            .ToListAsync(ct);
        var officialDict = officials.GroupBy(x => x.ObjectId)
            .ToDictionary(g => g.Key, g => g.First().Value);

        return regions
            .Select(r => new RegionDto(
                r.ObjectId,
                r.ObjectGuid,
                (officialDict.GetValueOrDefault(r.ObjectId) ?? r.Name)?.Trim() ?? string.Empty,
                r.TypeName?.Trim()))
            .OrderBy(r => r.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), false))
            .ToList();
    }

    public async Task<PagedResult<HouseSummaryDto>> GetHousesByStreetAsync(
        long streetObjectId, string? numFilter, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var childIds = await ChildObjectIdsAsync(streetObjectId, LevelHouse, ct);
        if (childIds.Count == 0)
            return new PagedResult<HouseSummaryDto>(Array.Empty<HouseSummaryDto>(), 0, page, pageSize);

        var houses = await db.Houses.AsNoTracking()
            .Where(h => childIds.Contains(h.ObjectId) && h.IsActual == true && h.IsActive == true)
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(numFilter))
        {
            var trimmed = numFilter.Trim();
            houses = houses
                .Where(h => h.HouseNum?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        var houseTypes = await db.HouseTypes.AsNoTracking()
            .Where(t => t.IsActive == true)
            .ToDictionaryAsync(t => t.Id, t => t.ShortName?.Trim() ?? string.Empty, ct);

        var ordered = houses
            .OrderBy(h => h.HouseNum, NaturalStringComparer.Instance)
            .ToList();
        var total = ordered.Count;
        var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(h => new HouseSummaryDto(
                h.ObjectId,
                h.ObjectGuid,
                h.HouseNum,
                h.AddNum1,
                h.AddNum2,
                h.HouseType,
                BuildHouseFullName(h, houseTypes)))
            .ToList();

        return new PagedResult<HouseSummaryDto>(pageItems, total, page, pageSize);
    }

    public async Task<PagedResult<ApartmentSummaryDto>> GetApartmentsByHouseAsync(
        long houseObjectId, string? numFilter, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var childIds = await ChildObjectIdsAsync(houseObjectId, LevelApartment, ct);
        if (childIds.Count == 0)
            return new PagedResult<ApartmentSummaryDto>(Array.Empty<ApartmentSummaryDto>(), 0, page, pageSize);

        var apartments = await db.Apartments.AsNoTracking()
            .Where(a => childIds.Contains(a.ObjectId) && a.IsActual == true && a.IsActive == true)
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(numFilter))
        {
            var trimmed = numFilter.Trim();
            apartments = apartments
                .Where(a => a.Number?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        var apartmentTypes = await db.ApartmentTypes.AsNoTracking()
            .Where(t => t.IsActive == true)
            .ToDictionaryAsync(t => t.Id, t => t.ShortName?.Trim() ?? string.Empty, ct);

        var ordered = apartments.OrderBy(a => a.Number, NaturalStringComparer.Instance).ToList();
        var total = ordered.Count;
        var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a =>
            {
                var typeShort = a.ApartType is { } at ? apartmentTypes.GetValueOrDefault(at) ?? string.Empty : string.Empty;
                var num = a.Number?.Trim() ?? string.Empty;
                var full = string.IsNullOrEmpty(typeShort) ? num : $"{typeShort} {num}";
                return new ApartmentSummaryDto(a.ObjectId, a.ObjectGuid, a.Number, a.ApartType, full);
            })
            .ToList();

        return new PagedResult<ApartmentSummaryDto>(pageItems, total, page, pageSize);
    }

    public async Task<PagedResult<RoomSummaryDto>> GetRoomsByApartmentAsync(
        long apartmentObjectId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var childIds = await ChildObjectIdsAsync(apartmentObjectId, LevelRoom, ct);
        if (childIds.Count == 0)
            return new PagedResult<RoomSummaryDto>(Array.Empty<RoomSummaryDto>(), 0, page, pageSize);

        var rooms = await db.Rooms.AsNoTracking()
            .Where(r => childIds.Contains(r.ObjectId) && r.IsActual == true && r.IsActive == true)
            .ToListAsync(ct);

        var roomTypes = await db.RoomTypes.AsNoTracking()
            .Where(t => t.IsActive == true)
            .ToDictionaryAsync(t => t.Id, t => t.ShortName?.Trim() ?? string.Empty, ct);

        var ordered = rooms.OrderBy(r => r.Number, NaturalStringComparer.Instance).ToList();
        var total = ordered.Count;
        var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r =>
            {
                var typeShort = r.RoomType is { } rt ? roomTypes.GetValueOrDefault(rt) ?? string.Empty : string.Empty;
                var num = r.Number?.Trim() ?? string.Empty;
                var full = string.IsNullOrEmpty(typeShort) ? num : $"{typeShort} {num}";
                return new RoomSummaryDto(r.ObjectId, r.ObjectGuid, r.Number, r.RoomType, full);
            })
            .ToList();

        return new PagedResult<RoomSummaryDto>(pageItems, total, page, pageSize);
    }

    private async Task<List<long>> ChildObjectIdsAsync(long parentObjectId, int childLevel, CancellationToken ct)
    {
        var ids = await db.AdmHierarchy.AsNoTracking()
            .Where(h => h.ParentObjId == parentObjectId && h.IsActive == true)
            .Select(h => h.ObjectId)
            .ToListAsync(ct);
        if (ids.Count == 0) return ids;

        // Фильтр по уровню для надёжности (на случай, если в иерархии встречаются разные).
        return await db.ReestrObjects.AsNoTracking()
            .Where(r => ids.Contains(r.ObjectId) && r.LevelId == childLevel && r.IsActive == true)
            .Select(r => r.ObjectId)
            .ToListAsync(ct);
    }

    private static string BuildHouseFullName(House h, Dictionary<int, string> houseTypes)
    {
        var typeShort = h.HouseType is { } ht ? houseTypes.GetValueOrDefault(ht) ?? string.Empty : string.Empty;
        var num = (h.HouseNum ?? string.Empty).Trim();
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(typeShort)) parts.Add(typeShort);
        if (!string.IsNullOrEmpty(num)) parts.Add(num);
        if (!string.IsNullOrEmpty(h.AddNum1)) parts.Add(h.AddNum1.Trim());
        if (!string.IsNullOrEmpty(h.AddNum2)) parts.Add(h.AddNum2.Trim());
        return string.Join(" ", parts);
    }

    /// <summary>Сравнение строк-номеров «по-человечески»: «2» меньше «10».</summary>
    private sealed class NaturalStringComparer : IComparer<string?>
    {
        public static readonly NaturalStringComparer Instance = new();
        public int Compare(string? x, string? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            int ix = 0, iy = 0;
            while (ix < x.Length && iy < y.Length)
            {
                if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
                {
                    long nx = 0, ny = 0;
                    while (ix < x.Length && char.IsDigit(x[ix])) { nx = nx * 10 + (x[ix] - '0'); ix++; }
                    while (iy < y.Length && char.IsDigit(y[iy])) { ny = ny * 10 + (y[iy] - '0'); iy++; }
                    if (nx != ny) return nx.CompareTo(ny);
                }
                else
                {
                    var c = x[ix].CompareTo(y[iy]);
                    if (c != 0) return c;
                    ix++; iy++;
                }
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
