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
    Param,
    ChangeHistory,
    AddhouseTypes
}

public static class FiasEntityKindResolver
{
    private static readonly (string Prefix, FiasEntityKind Kind)[] Map =
    [
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
        ("AS_PARAM_",              FiasEntityKind.Param),
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
