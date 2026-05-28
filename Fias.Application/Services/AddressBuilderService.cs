using System.Globalization;
using Fias.Application.Abstractions;
using Fias.Application.Models;
using Fias.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fias.Application.Services;

public class AddressBuilderService(IFiasDbContext db) : IAddressBuilderService
{
    // Уровни ФИАС (см. OBJECT_LEVELS): 1..8 — адресообразующие, 9 — земельный участок,
    // 10 — здание, 11 — помещение, 12 — комната, 17 — машино-место.
    private const int LevelLand = 9;
    private const int LevelHouse = 10;
    private const int LevelApartment = 11;
    private const int LevelRoom = 12;
    private const int LevelCarplace = 17;

    /// <summary>TYPEID параметра «Официальное наименование» в PARAM.</summary>
    private const int OfficialNameTypeId = 16;

    public async Task<AddressDto?> BuildByObjectIdAsync(long objectId, CancellationToken ct)
    {
        var hierarchy = await db.AdmHierarchy.AsNoTracking()
            .Where(h => h.ObjectId == objectId && h.IsActive == 1)
            .Select(h => new { h.ObjectId, h.Path })
            .FirstOrDefaultAsync(ct);

        return hierarchy?.Path is null
            ? null
            : await BuildByPathAsync(hierarchy.ObjectId, hierarchy.Path, ct);
    }

    public async Task<AddressDto?> BuildByObjectGuidAsync(Guid objectGuid, CancellationToken ct)
    {
        var objectId = await db.AddressObjects.AsNoTracking()
            .Where(a => a.ObjectGuid == objectGuid && a.IsActual == 1 && a.IsActive == 1)
            .Select(a => (long?)a.ObjectId)
            .FirstOrDefaultAsync(ct);

        return objectId is null ? null : await BuildByObjectIdAsync(objectId.Value, ct);
    }

    public async Task<AddressDto?> BuildByPathAsync(long leafObjectId, string path, CancellationToken ct)
    {
        var ids = ParsePath(path);
        if (ids.Count == 0) return null;

        // 1. Тип каждого уровня — из reestr_objects.
        var levels = await db.ReestrObjects.AsNoTracking()
            .Where(r => ids.Contains(r.ObjectId))
            .Select(r => new { r.ObjectId, r.LevelId, r.ObjectGuid })
            .ToDictionaryAsync(x => x.ObjectId, ct);

        // 2. Подгружаем основные данные по группам уровней одним запросом каждая.
        var addressList = await db.AddressObjects.AsNoTracking()
            .Where(a => a.IsActual == 1 && a.IsActive == 1 && ids.Contains(a.ObjectId))
            .ToListAsync(ct);
        var addressDict = addressList.GroupBy(a => a.ObjectId).ToDictionary(g => g.Key, g => g.First());

        var houseList = await db.Houses.AsNoTracking()
            .Where(h => h.IsActual == 1 && h.IsActive == 1 && ids.Contains(h.ObjectId))
            .ToListAsync(ct);
        var houseDict = houseList.GroupBy(h => h.ObjectId).ToDictionary(g => g.Key, g => g.First());

        var apartmentList = await db.Apartments.AsNoTracking()
            .Where(a => a.IsActual == 1 && a.IsActive == 1 && ids.Contains(a.ObjectId))
            .ToListAsync(ct);
        var apartmentDict = apartmentList.GroupBy(a => a.ObjectId).ToDictionary(g => g.Key, g => g.First());

        var roomList = await db.Rooms.AsNoTracking()
            .Where(r => r.IsActual == 1 && r.IsActive == 1 && ids.Contains(r.ObjectId))
            .ToListAsync(ct);
        var roomDict = roomList.GroupBy(r => r.ObjectId).ToDictionary(g => g.Key, g => g.First());

        // 3. Параметры «Официальное наименование» (TYPEID=16) — для Level 1, 3, 4.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var officialNames = await db.Params.AsNoTracking()
            .Where(p => ids.Contains(p.ObjectId)
                        && p.TypeId == OfficialNameTypeId
                        && (p.EndDate == null || p.EndDate > today))
            .GroupBy(p => p.ObjectId)
            .Select(g => new { ObjectId = g.Key, Value = g.OrderByDescending(p => p.StartDate).First().Value })
            .ToDictionaryAsync(x => x.ObjectId, x => x.Value, ct);

        // 4. Справочники типов — небольшие, грузим целиком в память (активные записи).
        var addressTypes = await db.AddressObjectTypes.AsNoTracking()
            .Where(t => t.IsActive == 1)
            .ToListAsync(ct);
        var houseTypes = await db.HouseTypes.AsNoTracking().Where(t => t.IsActive == 1).ToDictionaryAsync(t => t.Id, ct);
        var apartmentTypes = await db.ApartmentTypes.AsNoTracking().Where(t => t.IsActive == 1).ToDictionaryAsync(t => t.Id, ct);
        var roomTypes = await db.RoomTypes.AsNoTracking().Where(t => t.IsActive == 1).ToDictionaryAsync(t => t.Id, ct);

        // 5. Собираем иерархию по порядку из PATH.
        var items = new List<AddressHierarchyItemDto>(ids.Count);
        foreach (var id in ids)
        {
            if (!levels.TryGetValue(id, out var lvl) || lvl.LevelId is null)
                continue;

            var level = lvl.LevelId.Value;
            var (typeFull, typeShort, name) = ResolveNameAndType(
                id, level, addressDict, houseDict, apartmentDict, roomDict,
                addressTypes, houseTypes, apartmentTypes, roomTypes);

            // Официальное наименование перекрывает type + name, если оно активно.
            string fullName;
            if (officialNames.TryGetValue(id, out var official) && !string.IsNullOrWhiteSpace(official))
            {
                fullName = official;
            }
            else
            {
                fullName = string.IsNullOrEmpty(typeFull)
                    ? name ?? string.Empty
                    : string.IsNullOrEmpty(name) ? typeFull : $"{typeFull} {name}";
            }

            items.Add(new AddressHierarchyItemDto(id, lvl.ObjectGuid, level, typeFull, typeShort, name, fullName));
        }

        if (items.Count == 0) return null;

        var leaf = items[^1];
        var fullAddress = string.Join(", ", items.Select(i => i.FullName));
        var shortAddress = string.Join(", ", items.Select(i =>
            string.IsNullOrEmpty(i.ShortType) ? i.Name ?? string.Empty : $"{i.ShortType} {i.Name}"));

        return new AddressDto(leaf.ObjectId, leaf.ObjectGuid, leaf.Level, fullAddress, shortAddress, items);
    }

    private static (string? typeFull, string? typeShort, string? name) ResolveNameAndType(
        long objectId,
        int level,
        Dictionary<long, AddressObject> addrs,
        Dictionary<long, House> houses,
        Dictionary<long, Apartment> apartments,
        Dictionary<long, Room> rooms,
        List<AddressObjectType> addressTypes,
        Dictionary<int, HouseType> houseTypes,
        Dictionary<int, ApartmentType> apartmentTypes,
        Dictionary<int, RoomType> roomTypes)
    {
        switch (level)
        {
            case LevelHouse when houses.TryGetValue(objectId, out var house):
            {
                var typeShort = house.HouseType is { } ht && houseTypes.TryGetValue(ht, out var t1) ? t1.ShortName : null;
                var typeFull = house.HouseType is { } ht2 && houseTypes.TryGetValue(ht2, out var t2) ? t2.Name : null;
                var name = BuildHouseName(house);
                return (typeFull, typeShort, name);
            }
            case LevelApartment when apartments.TryGetValue(objectId, out var apt):
            {
                var typeShort = apt.ApartType is { } at && apartmentTypes.TryGetValue(at, out var t1) ? t1.ShortName : null;
                var typeFull = apt.ApartType is { } at2 && apartmentTypes.TryGetValue(at2, out var t2) ? t2.Name : null;
                return (typeFull, typeShort, apt.Number);
            }
            case LevelRoom when rooms.TryGetValue(objectId, out var room):
            {
                var typeShort = room.RoomType is { } rt && roomTypes.TryGetValue(rt, out var t1) ? t1.ShortName : null;
                var typeFull = room.RoomType is { } rt2 && roomTypes.TryGetValue(rt2, out var t2) ? t2.Name : null;
                return (typeFull, typeShort, room.Number);
            }
            case LevelLand:
            case LevelCarplace:
            {
                var t = addressTypes.FirstOrDefault(x => x.Level == level);
                return (t?.Name, t?.ShortName, null);
            }
            default:
            {
                if (!addrs.TryGetValue(objectId, out var addr)) return (null, null, null);
                var t = addressTypes.FirstOrDefault(x => x.Level == level
                                                         && string.Equals(x.ShortName, addr.TypeName, StringComparison.OrdinalIgnoreCase));
                return (t?.Name, t?.ShortName ?? addr.TypeName, addr.Name);
            }
        }
    }

    private static string BuildHouseName(House h)
    {
        // HOUSETYPE HOUSENUM [ADDTYPE1 ADDNUM1] [ADDTYPE2 ADDNUM2]
        // Доп. типы (ADDTYPE1/2) — это идентификаторы из ADDHOUSE_TYPES, который мы пока не импортируем.
        // Поэтому используем числовой код доп. типа как fallback.
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(h.HouseNum)) parts.Add(h.HouseNum);
        if (!string.IsNullOrEmpty(h.AddNum1)) parts.Add(h.AddType1?.ToString(CultureInfo.InvariantCulture) + " " + h.AddNum1);
        if (!string.IsNullOrEmpty(h.AddNum2)) parts.Add(h.AddType2?.ToString(CultureInfo.InvariantCulture) + " " + h.AddNum2);
        return string.Join(" ", parts);
    }

    private static List<long> ParsePath(string path)
    {
        var result = new List<long>();
        foreach (var token in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                result.Add(id);
        }
        return result;
    }
}
