using System.Globalization;
using Dapper;
using Fias.Application.Abstractions;
using Fias.Application.Models;
using Fias.Domain.Entities;

namespace Fias.Application.Services;

public class HierarchyService(ISqlConnectionFactory factory) : IHierarchyService
{
    private const int LevelRegion = 1;
    private const int LevelHouse = 10;
    private const int LevelApartment = 11;
    private const int LevelRoom = 12;

    public async Task<IReadOnlyList<RegionDto>> GetRegionsAsync(CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);

        var regions = (await conn.QueryAsync<RegionRow>(new CommandDefinition("""
            SELECT a.objectid, a.objectguid, a.name, a.typename
            FROM fias.addressobjects a
            JOIN fias.reestr_objects r ON r.objectid = a.objectid AND r.levelid = 1 AND r.isactive = true
            WHERE a.level = 1 AND a.isactual = true AND a.isactive = true
            """, cancellationToken: ct))).AsList();

        // «Официальное наименование» (typeid=16) может перекрывать NAME для субъектов.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var officials = await conn.QueryAsync<OfficialRow>(new CommandDefinition("""
            SELECT p.objectid, p.value
            FROM fias.params p
            WHERE p.typeid = 16 AND (p.enddate IS NULL OR p.enddate > @today)
              AND p.objectid IN (SELECT objectid FROM fias.reestr_objects WHERE levelid = 1 AND isactive = true)
            ORDER BY p.objectid, p.startdate DESC NULLS LAST
            """, new { today }, cancellationToken: ct));

        var officialDict = officials
            .GroupBy(x => x.ObjectId)
            .ToDictionary(g => g.Key, g => g.First().Value);

        return regions
            .Select(r => new RegionDto(
                r.ObjectId,
                r.ObjectGuid,
                (officialDict.GetValueOrDefault(r.ObjectId) ?? r.Name)?.Trim() ?? string.Empty,
                r.TypeName?.Trim()))
            .OrderBy(r => r.Name, StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), false))
            .ToList();
    }

    public async Task<PagedResult<HouseSummaryDto>> GetHousesByStreetAsync(
        long streetObjectId, string? numFilter, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);

        await using var conn = await factory.OpenAsync(ct);

        // Дом привязан к улице через adm_hierarchy.parentobjid — один JOIN, без выборки id в приложение.
        var houses = (await conn.QueryAsync<House>(new CommandDefinition("""
            SELECT h.*
            FROM fias.houses h
            JOIN fias.adm_hierarchy ah ON ah.objectid = h.objectid AND ah.isactive = true AND ah.parentobjid = @street
            JOIN fias.reestr_objects r ON r.objectid = h.objectid AND r.levelid = 10 AND r.isactive = true
            WHERE h.isactual = true AND h.isactive = true
            """, new { street = streetObjectId }, cancellationToken: ct))).AsList();

        if (houses.Count == 0)
            return new PagedResult<HouseSummaryDto>(Array.Empty<HouseSummaryDto>(), 0, page, pageSize);

        if (!string.IsNullOrWhiteSpace(numFilter))
        {
            var trimmed = numFilter.Trim();
            houses = houses
                .Where(h => h.HouseNum?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        var houseTypes = await LoadTypesAsync(conn, "fias.house_types", ct);

        var ordered = houses.OrderBy(h => h.HouseNum, NaturalStringComparer.Instance).ToList();
        var total = ordered.Count;
        var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(h => new HouseSummaryDto(
                h.ObjectId, h.ObjectGuid, h.HouseNum, h.AddNum1, h.AddNum2, h.HouseType,
                BuildHouseFullName(h, houseTypes)))
            .ToList();

        return new PagedResult<HouseSummaryDto>(pageItems, total, page, pageSize);
    }

    public async Task<PagedResult<ApartmentSummaryDto>> GetApartmentsByHouseAsync(
        long houseObjectId, string? numFilter, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);

        await using var conn = await factory.OpenAsync(ct);

        var apartments = (await conn.QueryAsync<Apartment>(new CommandDefinition("""
            SELECT a.*
            FROM fias.apartments a
            JOIN fias.adm_hierarchy ah ON ah.objectid = a.objectid AND ah.isactive = true AND ah.parentobjid = @house
            JOIN fias.reestr_objects r ON r.objectid = a.objectid AND r.levelid = 11 AND r.isactive = true
            WHERE a.isactual = true AND a.isactive = true
            """, new { house = houseObjectId }, cancellationToken: ct))).AsList();

        if (apartments.Count == 0)
            return new PagedResult<ApartmentSummaryDto>(Array.Empty<ApartmentSummaryDto>(), 0, page, pageSize);

        if (!string.IsNullOrWhiteSpace(numFilter))
        {
            var trimmed = numFilter.Trim();
            apartments = apartments
                .Where(a => a.Number?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        var apartmentTypes = await LoadTypesAsync(conn, "fias.apartment_types", ct);

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

        await using var conn = await factory.OpenAsync(ct);

        var rooms = (await conn.QueryAsync<Room>(new CommandDefinition("""
            SELECT r.*
            FROM fias.rooms r
            JOIN fias.adm_hierarchy ah ON ah.objectid = r.objectid AND ah.isactive = true AND ah.parentobjid = @apartment
            JOIN fias.reestr_objects ro ON ro.objectid = r.objectid AND ro.levelid = 12 AND ro.isactive = true
            WHERE r.isactual = true AND r.isactive = true
            """, new { apartment = apartmentObjectId }, cancellationToken: ct))).AsList();

        if (rooms.Count == 0)
            return new PagedResult<RoomSummaryDto>(Array.Empty<RoomSummaryDto>(), 0, page, pageSize);

        var roomTypes = await LoadTypesAsync(conn, "fias.room_types", ct);

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

    /// <summary>Справочник «id → краткий тип» из *_types таблицы.</summary>
    private static async Task<Dictionary<int, string>> LoadTypesAsync(
        System.Data.Common.DbConnection conn, string table, CancellationToken ct)
    {
        var rows = await conn.QueryAsync<(int Id, string? ShortName)>(new CommandDefinition(
            $"SELECT id, shortname FROM {table} WHERE isactive = true", cancellationToken: ct));
        return rows.ToDictionary(x => x.Id, x => x.ShortName?.Trim() ?? string.Empty);
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

    private readonly record struct RegionRow(long ObjectId, Guid? ObjectGuid, string? Name, string? TypeName);
    private readonly record struct OfficialRow(long ObjectId, string? Value);

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
