namespace Fias.Service.Updater.Services.Archives;

/// <summary>
/// Классификатор файлов внутри ZIP-выгрузки ФИАС.
/// Все файлы в ГАР именуются по шаблону AS_&lt;ENTITY&gt;_&lt;date&gt;_&lt;guid&gt;.XML.
/// </summary>
public enum FiasEntityKind
{
    Unknown,
    ReestrObjects,
    AddressObjects,
    Houses,
    Apartments,
    Rooms,
    Steads,
    Carplaces,
    MunHierarchy,
    AdmHierarchy,
    AddressObjectTypes,
    HouseTypes,
    ApartmentTypes,
    RoomTypes,
    ObjectLevels,
    // Параметры разбиты по семействам объектов: XML-ID уникален только внутри семейства,
    // между семействами ID пересекаются, поэтому в БД ключ составной (objtype, id).
    ParamAddrObj,
    ParamHouses,
    ParamApartments,
    ParamRooms,
    ParamSteads,
    ParamCarplaces,
    ChangeHistory,
    AddhouseTypes
}

public static class FiasEntityKindResolver
{
    private static readonly (string Prefix, FiasEntityKind Kind)[] Map =
    [
        // Параметры (PARAMS) — все семейства идут в одну таблицу fias.params.
        // ВАЖНО: эти префиксы должны стоять РАНЬШЕ объектных (AS_ADDR_OBJ_, AS_HOUSES_ и т.п.),
        // иначе, например, AS_ADDR_OBJ_PARAMS перехватится как AS_ADDR_OBJ_. Справочник
        // AS_PARAM_TYPES не маршрутизируем сюда — он не входит в базовый набор.
        ("AS_ADDR_OBJ_PARAMS_",    FiasEntityKind.ParamAddrObj),
        ("AS_HOUSES_PARAMS_",      FiasEntityKind.ParamHouses),
        ("AS_APARTMENTS_PARAMS_",  FiasEntityKind.ParamApartments),
        ("AS_ROOMS_PARAMS_",       FiasEntityKind.ParamRooms),
        ("AS_STEADS_PARAMS_",      FiasEntityKind.ParamSteads),
        ("AS_CARPLACES_PARAMS_",   FiasEntityKind.ParamCarplaces),

        ("AS_REESTR_OBJECTS_",     FiasEntityKind.ReestrObjects),
        ("AS_ADDR_OBJ_TYPES_",     FiasEntityKind.AddressObjectTypes),
        ("AS_ADDR_OBJ_",           FiasEntityKind.AddressObjects),
        ("AS_HOUSE_TYPES_",        FiasEntityKind.HouseTypes),
        ("AS_HOUSES_",             FiasEntityKind.Houses),
        ("AS_APARTMENT_TYPES_",    FiasEntityKind.ApartmentTypes),
        ("AS_APARTMENTS_",         FiasEntityKind.Apartments),
        ("AS_ROOM_TYPES_",         FiasEntityKind.RoomTypes),
        ("AS_ROOMS_",              FiasEntityKind.Rooms),
        ("AS_STEADS_",             FiasEntityKind.Steads),
        ("AS_CARPLACES_",          FiasEntityKind.Carplaces),
        ("AS_MUN_HIERARCHY_",      FiasEntityKind.MunHierarchy),
        ("AS_ADM_HIERARCHY_",      FiasEntityKind.AdmHierarchy),
        ("AS_OBJECT_LEVELS_",      FiasEntityKind.ObjectLevels),
        ("AS_CHANGE_HISTORY_",     FiasEntityKind.ChangeHistory),
        ("AS_ADDHOUSE_TYPES_",     FiasEntityKind.AddhouseTypes),
    ];

    public static FiasEntityKind Resolve(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var (prefix, kind) in Map)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return kind;
        }
        return FiasEntityKind.Unknown;
    }
}
