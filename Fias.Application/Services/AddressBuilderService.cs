using System.Globalization;
using Dapper;
using Fias.Application.Abstractions;
using Fias.Application.Models;
using Fias.Domain.Entities;

namespace Fias.Application.Services;

public class AddressBuilderService(ISqlConnectionFactory factory) : IAddressBuilderService
{
    // Уровни ФИАС (см. OBJECT_LEVELS): 1..8 — адресообразующие, 9 — земельный участок,
    // 10 — здание, 11 — помещение, 12 — комната, 17 — машино-место.
    private const int LevelRegion = 1;
    private const int LevelAdmArea = 2;
    private const int LevelLand = 9;
    private const int LevelHouse = 10;
    private const int LevelApartment = 11;
    private const int LevelRoom = 12;
    private const int LevelCarplace = 17;

    // TYPEID параметров из справочника ГАР AS_PARAM_TYPES.
    private const int PtIfnsFl = 1;
    private const int PtIfnsUl = 2;
    private const int PtPostal = 5;
    private const int PtOkato = 6;
    private const int PtOktmo = 7;
    private const int PtCadastr = 8;
    private const int PtKladr = 11;       // PLAINCODE — код КЛАДР без признака актуальности.
    private const int PtRegionCode = 12;
    private const int PtOfficial = 16;    // Официальное наименование (обычно для субъектов РФ).
    private const int PtOktmoBudget = 21;

    /// <summary>Тип адресации: 1 — административное деление (строим по adm_hierarchy).</summary>
    private const int AdmAddressType = 1;

    public async Task<AddressDto?> BuildByObjectIdAsync(long objectId, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<HierRow>(new CommandDefinition(
            "SELECT objectid, path FROM fias.adm_hierarchy WHERE objectid = @objectId AND isactive = true LIMIT 1",
            new { objectId }, cancellationToken: ct));

        return row.Path is null ? null : await BuildByPathAsync(row.ObjectId, row.Path, ct);
    }

    public async Task<AddressDto?> BuildByObjectGuidAsync(Guid objectGuid, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        var objectId = await conn.QueryFirstOrDefaultAsync<long?>(new CommandDefinition(
            "SELECT objectid FROM fias.addressobjects WHERE objectguid = @objectGuid AND isactual = true AND isactive = true LIMIT 1",
            new { objectGuid }, cancellationToken: ct));

        return objectId is null ? null : await BuildByObjectIdAsync(objectId.Value, ct);
    }

    public async Task<AddressDto?> BuildByPathAsync(long leafObjectId, string path, CancellationToken ct)
    {
        var ids = ParsePath(path);
        if (ids.Count == 0) return null;

        await using var conn = await factory.OpenAsync(ct);

        // 1. Тип каждого уровня — из reestr_objects (PK objectid -> уникально).
        var levels = (await conn.QueryAsync<LevelRow>(new CommandDefinition(
                "SELECT objectid, levelid, objectguid FROM fias.reestr_objects WHERE objectid IN @ids",
                new { ids }, cancellationToken: ct)))
            .ToDictionary(x => x.ObjectId);

        // 2. Основные данные по группам уровней — по одному запросу каждая.
        var addressList = (await conn.QueryAsync<AddressObject>(new CommandDefinition(
            "SELECT * FROM fias.addressobjects WHERE isactual = true AND isactive = true AND objectid IN @ids",
            new { ids }, cancellationToken: ct))).AsList();
        var addressDict = addressList.GroupBy(a => a.ObjectId).ToDictionary(g => g.Key, g => g.First());

        var houseList = (await conn.QueryAsync<House>(new CommandDefinition(
            "SELECT * FROM fias.houses WHERE isactual = true AND isactive = true AND objectid IN @ids",
            new { ids }, cancellationToken: ct))).AsList();
        var houseDict = houseList.GroupBy(h => h.ObjectId).ToDictionary(g => g.Key, g => g.First());

        var apartmentList = (await conn.QueryAsync<Apartment>(new CommandDefinition(
            "SELECT * FROM fias.apartments WHERE isactual = true AND isactive = true AND objectid IN @ids",
            new { ids }, cancellationToken: ct))).AsList();
        var apartmentDict = apartmentList.GroupBy(a => a.ObjectId).ToDictionary(g => g.Key, g => g.First());

        var roomList = (await conn.QueryAsync<Room>(new CommandDefinition(
            "SELECT * FROM fias.rooms WHERE isactual = true AND isactive = true AND objectid IN @ids",
            new { ids }, cancellationToken: ct))).AsList();
        var roomDict = roomList.GroupBy(r => r.ObjectId).ToDictionary(g => g.Key, g => g.First());

        // 3. Параметры (PARAM) по нужным TYPEID — индекс, ОКАТО, ОКТМО, КЛАДР, кадастр и т.д.
        //    Сортировка по StartDate/Id desc — берём актуальное значение каждого (object, typeid).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var paramRows = await conn.QueryAsync<ParamRow>(new CommandDefinition("""
            SELECT objectid, typeid, value
            FROM fias.params
            WHERE objectid IN @ids
              AND typeid IN (1, 2, 5, 6, 7, 8, 11, 12, 16, 21)
              AND (enddate IS NULL OR enddate > @today)
            ORDER BY objectid, typeid, startdate DESC NULLS LAST, id DESC
            """, new { ids, today }, cancellationToken: ct));

        var paramsByObj = new Dictionary<long, Dictionary<int, string>>();
        foreach (var row in paramRows)
        {
            if (row.TypeId is not int tid || string.IsNullOrWhiteSpace(row.Value)) continue;
            if (!paramsByObj.TryGetValue(row.ObjectId, out var byType))
                paramsByObj[row.ObjectId] = byType = new Dictionary<int, string>();
            byType.TryAdd(tid, row.Value.Trim()); // первое = актуальное (благодаря сортировке)
        }

        // 4. Справочники типов — небольшие, грузим целиком (активные записи).
        var addressTypeList = (await conn.QueryAsync<AddressObjectType>(new CommandDefinition(
            "SELECT * FROM fias.addressobject_types WHERE isactive = true", cancellationToken: ct))).AsList();
        var addressTypeByLevelShort = addressTypeList
            .Where(t => t.Level is not null && !string.IsNullOrWhiteSpace(t.ShortName))
            .GroupBy(t => (t.Level!.Value, t.ShortName!.Trim()), AddressTypeKeyComparer.Instance)
            .ToDictionary(g => g.Key, g => g.First(), AddressTypeKeyComparer.Instance);
        var addressTypeByLevel = addressTypeList
            .Where(t => t.Level is not null)
            .GroupBy(t => t.Level!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var houseTypes = (await conn.QueryAsync<HouseType>(new CommandDefinition(
            "SELECT * FROM fias.house_types WHERE isactive = true", cancellationToken: ct))).ToDictionary(t => t.Id);
        var apartmentTypes = (await conn.QueryAsync<ApartmentType>(new CommandDefinition(
            "SELECT * FROM fias.apartment_types WHERE isactive = true", cancellationToken: ct))).ToDictionary(t => t.Id);
        var roomTypes = (await conn.QueryAsync<RoomType>(new CommandDefinition(
            "SELECT * FROM fias.room_types WHERE isactive = true", cancellationToken: ct))).ToDictionary(t => t.Id);

        // 5. Собираем иерархию по порядку из PATH.
        var items = new List<AddressHierarchyItemDto>(ids.Count);
        int? regionCode = null;

        foreach (var id in ids)
        {
            if (!levels.TryGetValue(id, out var lvl) || lvl.LevelId is null)
                continue;

            var level = lvl.LevelId.Value;
            var objParams = paramsByObj.GetValueOrDefault(id);
            var official = objParams?.GetValueOrDefault(PtOfficial);
            var kladr = objParams?.GetValueOrDefault(PtKladr);

            var (typeFull, typeShort, name) = ResolveNameAndType(
                id, level, addressDict, houseDict, apartmentDict, roomDict,
                addressTypeByLevelShort, addressTypeByLevel, houseTypes, apartmentTypes, roomTypes);

            var (fullName, fullNameShort) = ComposeNames(level, typeFull, typeShort, name, official);
            var objectType = ObjectType(level);

            if (level == LevelRegion)
                regionCode = ResolveRegionCode(objParams, kladr);

            if (objectType == "house" && houseDict.TryGetValue(id, out var house))
            {
                items.Add(new AddressHierarchyItemDto(
                    ObjectType: objectType,
                    ObjectId: id,
                    ObjectLevelId: level,
                    ObjectGuid: lvl.ObjectGuid,
                    FullName: fullName,
                    FullNameShort: fullNameShort,
                    HierarchyPlace: HierarchyPlace(level),
                    TypeName: typeFull,
                    TypeShortName: typeShort,
                    Number: house.HouseNum?.Trim() ?? string.Empty,
                    AddNumber1: house.AddNum1?.Trim() ?? string.Empty,
                    AddType1Name: string.Empty,
                    AddType1ShortName: string.Empty,
                    AddNumber2: house.AddNum2?.Trim() ?? string.Empty,
                    AddType2Name: string.Empty,
                    AddType2ShortName: string.Empty));
            }
            else if (objectType is "apartment" or "room")
            {
                items.Add(new AddressHierarchyItemDto(
                    ObjectType: objectType,
                    ObjectId: id,
                    ObjectLevelId: level,
                    ObjectGuid: lvl.ObjectGuid,
                    FullName: fullName,
                    FullNameShort: fullNameShort,
                    HierarchyPlace: HierarchyPlace(level),
                    TypeName: typeFull,
                    TypeShortName: typeShort,
                    Number: name ?? string.Empty));
            }
            else
            {
                items.Add(new AddressHierarchyItemDto(
                    ObjectType: objectType,
                    ObjectId: id,
                    ObjectLevelId: level,
                    ObjectGuid: lvl.ObjectGuid,
                    FullName: fullName,
                    FullNameShort: fullNameShort,
                    HierarchyPlace: HierarchyPlace(level),
                    TypeName: typeFull,
                    TypeShortName: typeShort,
                    TypeFormCode: 0,
                    Name: name,
                    RegionCode: level == LevelRegion ? regionCode : null,
                    KladrCode: kladr));
            }
        }

        if (items.Count == 0) return null;

        var leaf = items[^1];
        var leafLevel = leaf.ObjectLevelId;
        var fullAddress = string.Join(", ", items.Select(i => i.FullName));
        var leafParams = paramsByObj.GetValueOrDefault(leaf.ObjectId);

        return new AddressDto(
            ObjectId: leaf.ObjectId,
            ObjectLevelId: leafLevel,
            OperationTypeId: ResolveOperationType(leaf.ObjectId, leafLevel, addressDict, houseDict, apartmentDict, roomDict),
            ObjectGuid: leaf.ObjectGuid,
            AddressType: AdmAddressType,
            FullName: fullAddress,
            RegionCode: regionCode,
            IsActive: true,
            Path: path,
            AddressDetails: BuildDetails(leafParams),
            Hierarchy: items,
            FederalDistrict: FederalDistrictCatalog.ByRegionCode(regionCode),
            HierarchyPlace: leaf.HierarchyPlace);
    }

    /// <summary>full_name и full_name_short по правилам ГАР: для региона/района тип после
    /// наименования, для прочих — перед. full_name использует полный тип (в нижнем регистре),
    /// short — сокращение. Официальное наименование (если есть) перекрывает оба.</summary>
    private static (string full, string fullShort) ComposeNames(
        int level, string? typeFull, string? typeShort, string? name, string? official)
    {
        if (!string.IsNullOrWhiteSpace(official))
            return (official.Trim(), official.Trim());

        var n = name?.Trim() ?? string.Empty;
        var full = typeFull?.Trim().ToLowerInvariant() ?? string.Empty;
        var shortType = typeShort?.Trim() ?? string.Empty;

        var postfix = level is LevelRegion or LevelAdmArea; // область/край/район — тип после имени
        return (Join(postfix, n, full), Join(postfix, n, shortType));
    }

    private static string Join(bool nameFirst, string name, string type)
    {
        if (string.IsNullOrEmpty(type)) return name;
        if (string.IsNullOrEmpty(name)) return type;
        return nameFirst ? $"{name} {type}" : $"{type} {name}";
    }

    private static string ObjectType(int level) => level switch
    {
        LevelRegion => "region",
        LevelHouse => "house",
        LevelApartment => "apartment",
        LevelRoom => "room",
        _ => "address_object"
    };

    /// <summary>
    /// Позиция элемента в адресной строке по правилам ГАР (для административного деления).
    /// Известные точки из эталона: L1→1, L2→2, L6→4, L8→6, L10→8.
    /// </summary>
    private static int HierarchyPlace(int level) => level switch
    {
        1 => 1,
        2 => 2,
        3 => 2,
        4 => 3,
        5 => 3,
        6 => 4,
        7 => 5,
        8 => 6,
        9 => 7,
        10 => 8,
        11 => 9,
        12 => 10,
        17 => 8,
        _ => level
    };

    private static int? ResolveRegionCode(Dictionary<int, string>? objParams, string? kladr)
    {
        if (objParams?.GetValueOrDefault(PtRegionCode) is { } rc
            && int.TryParse(rc.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        // Фолбэк: первые две цифры КЛАДР-кода субъекта.
        if (kladr is { Length: >= 2 } code
            && int.TryParse(code[..2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromKladr))
            return fromKladr;

        return null;
    }

    private static AddressDetailsDto? BuildDetails(Dictionary<int, string>? p)
    {
        if (p is null) return null;
        var details = new AddressDetailsDto(
            PostalCode: p.GetValueOrDefault(PtPostal),
            IfnsUl: p.GetValueOrDefault(PtIfnsUl),
            IfnsFl: p.GetValueOrDefault(PtIfnsFl),
            Okato: p.GetValueOrDefault(PtOkato),
            Oktmo: p.GetValueOrDefault(PtOktmo),
            CadastralNumber: p.GetValueOrDefault(PtCadastr),
            OktmoBudget: p.GetValueOrDefault(PtOktmoBudget));

        var empty = details is { PostalCode: null, IfnsUl: null, IfnsFl: null, Okato: null,
            Oktmo: null, CadastralNumber: null, OktmoBudget: null };
        return empty ? null : details;
    }

    private static int? ResolveOperationType(
        long id, int level,
        Dictionary<long, AddressObject> addrs, Dictionary<long, House> houses,
        Dictionary<long, Apartment> apartments, Dictionary<long, Room> rooms)
        => level switch
        {
            LevelHouse => houses.TryGetValue(id, out var h) ? h.OperTypeId : null,
            LevelApartment => apartments.TryGetValue(id, out var a) ? a.OperTypeId : null,
            LevelRoom => rooms.TryGetValue(id, out var r) ? r.OperTypeId : null,
            _ => addrs.TryGetValue(id, out var ao) ? ao.OperTypeId : null
        };

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
        // HOUSENUM [ADDNUM1] [ADDNUM2]. ADDHOUSE_TYPES не импортируем — доп. типы опускаем.
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(h.HouseNum)) parts.Add(h.HouseNum.Trim());
        if (!string.IsNullOrEmpty(h.AddNum1)) parts.Add(h.AddNum1.Trim());
        if (!string.IsNullOrEmpty(h.AddNum2)) parts.Add(h.AddNum2.Trim());
        return string.Join(" ", parts);
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

    // --- Строки Dapper-маппинга ---------------------------------------------
    private readonly record struct HierRow(long ObjectId, string? Path);
    private readonly record struct LevelRow(long ObjectId, int? LevelId, Guid? ObjectGuid);
    private readonly record struct ParamRow(long ObjectId, int? TypeId, string? Value);
}
