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
        //    Грузим плоско и группируем на клиенте: EF Core 8 не транслирует
        //    GroupBy → OrderByDescending().First() в одном запросе.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var officialRows = await db.Params.AsNoTracking()
            .Where(p => ids.Contains(p.ObjectId)
                        && p.TypeId == OfficialNameTypeId
                        && (p.EndDate == null || p.EndDate > today))
            .OrderBy(p => p.ObjectId).ThenByDescending(p => p.StartDate)
            .Select(p => new { p.ObjectId, p.Value })
            .ToListAsync(ct);
        var officialNames = officialRows
            .GroupBy(x => x.ObjectId)
            .ToDictionary(g => g.Key, g => g.First().Value);

        // 4. Справочники типов — небольшие, грузим целиком в память (активные записи).
        //    AddressObjectType индексируем по (Level, ShortName) для O(1) lookup;
        //    ShortName тримим — в данных ФИАС встречаются пробелы.
        var addressTypeList = await db.AddressObjectTypes.AsNoTracking()
            .Where(t => t.IsActive == 1)
            .ToListAsync(ct);
        var addressTypeByLevelShort = addressTypeList
            .Where(t => t.Level is not null && !string.IsNullOrWhiteSpace(t.ShortName))
            .GroupBy(t => (t.Level!.Value, t.ShortName!.Trim()), AddressTypeKeyComparer.Instance)
            .ToDictionary(g => g.Key, g => g.First(), AddressTypeKeyComparer.Instance);
        var addressTypeByLevel = addressTypeList
            .Where(t => t.Level is not null)
            .GroupBy(t => t.Level!.Value)
            .ToDictionary(g => g.Key, g => g.First());

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
                addressTypeByLevelShort, addressTypeByLevel, houseTypes, apartmentTypes, roomTypes);

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
        Dictionary<(int Level, string ShortName), AddressObjectType> addressTypeByLevelShort,
        Dictionary<int, AddressObjectType> addressTypeByLevel,
        Dictionary<int, HouseType> houseTypes,
        Dictionary<int, ApartmentType> apartmentTypes,
        Dictionary<int, RoomType> roomTypes)
    {
        switch (level)
        {
            case LevelHouse when houses.TryGetValue(objectId, out var house):
            {
                var (typeFull, typeShort) = house.HouseType is { } ht && houseTypes.TryGetValue(ht, out var t)
                    ? (t.Name?.Trim(), t.ShortName?.Trim())
                    : (null, null);
                return (typeFull, typeShort, BuildHouseName(house));
            }
            case LevelApartment when apartments.TryGetValue(objectId, out var apt):
            {
                var (typeFull, typeShort) = apt.ApartType is { } at && apartmentTypes.TryGetValue(at, out var t)
                    ? (t.Name?.Trim(), t.ShortName?.Trim())
                    : (null, null);
                return (typeFull, typeShort, apt.Number?.Trim());
            }
            case LevelRoom when rooms.TryGetValue(objectId, out var room):
            {
                var (typeFull, typeShort) = room.RoomType is { } rt && roomTypes.TryGetValue(rt, out var t)
                    ? (t.Name?.Trim(), t.ShortName?.Trim())
                    : (null, null);
                return (typeFull, typeShort, room.Number?.Trim());
            }
            case LevelLand:
            case LevelCarplace:
            {
                addressTypeByLevel.TryGetValue(level, out var t);
                return (t?.Name?.Trim(), t?.ShortName?.Trim(), null);
            }
            default:
            {
                // В ФИАС встречаются NAME/TYPENAME с лидирующими пробелами — нормализуем.
                if (!addrs.TryGetValue(objectId, out var addr)) return (null, null, null);
                var typeName = addr.TypeName?.Trim();
                var name = addr.Name?.Trim();
                if (!string.IsNullOrEmpty(typeName)
                    && addressTypeByLevelShort.TryGetValue((level, typeName), out var t))
                {
                    return (t.Name?.Trim(), t.ShortName?.Trim() ?? typeName, name);
                }
                return (null, typeName, name);
            }
        }
    }

    private static string BuildHouseName(House h)
    {
        // HOUSETYPE HOUSENUM [ADDTYPE1 ADDNUM1] [ADDTYPE2 ADDNUM2]
        // Доп. типы (ADDTYPE1/2) — это идентификаторы из ADDHOUSE_TYPES, который мы пока не импортируем.
        // Поэтому используем числовой код доп. типа как fallback (если есть).
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(h.HouseNum)) parts.Add(h.HouseNum);
        if (!string.IsNullOrEmpty(h.AddNum1)) parts.Add(JoinAdd(h.AddType1, h.AddNum1));
        if (!string.IsNullOrEmpty(h.AddNum2)) parts.Add(JoinAdd(h.AddType2, h.AddNum2));
        return string.Join(" ", parts);
    }

    private static string JoinAdd(int? type, string num)
    {
        return type is null
            ? num
            : $"{type.Value.ToString(CultureInfo.InvariantCulture)} {num}";
    }

    private sealed class AddressTypeKeyComparer : IEqualityComparer<(int Level, string ShortName)>
    {
        public static readonly AddressTypeKeyComparer Instance = new();
        public bool Equals((int Level, string ShortName) x, (int Level, string ShortName) y)
            => x.Level == y.Level && string.Equals(x.ShortName, y.ShortName, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((int Level, string ShortName) obj)
            => HashCode.Combine(obj.Level, obj.ShortName.ToLowerInvariant());
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
