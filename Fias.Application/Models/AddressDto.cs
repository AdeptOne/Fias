using System.Text.Json.Serialization;

namespace Fias.Application.Models;

/// <summary>Один уровень иерархии в адресной строке (формат ГАР).</summary>
public record AddressHierarchyItemDto(
    [property: JsonPropertyName("object_type")] string ObjectType,
    [property: JsonPropertyName("object_id")] long ObjectId,
    [property: JsonPropertyName("object_level_id")] int ObjectLevelId,
    [property: JsonPropertyName("object_guid")] Guid? ObjectGuid,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("full_name_short")] string FullNameShort,
    [property: JsonPropertyName("hierarchy_place")] int HierarchyPlace,
    [property: JsonPropertyName("type_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TypeName = null,
    [property: JsonPropertyName("type_short_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TypeShortName = null,
    [property: JsonPropertyName("type_form_code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TypeFormCode = null,
    [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonPropertyName("region_code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RegionCode = null,
    [property: JsonPropertyName("kladr_code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? KladrCode = null,
    // Поля дома (object_type = "house").
    [property: JsonPropertyName("number"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Number = null,
    [property: JsonPropertyName("add_number1"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddNumber1 = null,
    [property: JsonPropertyName("add_type1_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddType1Name = null,
    [property: JsonPropertyName("add_type1_short_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddType1ShortName = null,
    [property: JsonPropertyName("add_number2"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddNumber2 = null,
    [property: JsonPropertyName("add_type2_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddType2Name = null,
    [property: JsonPropertyName("add_type2_short_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AddType2ShortName = null);

/// <summary>Дополнительные коды объекта адресации (из PARAM).</summary>
public record AddressDetailsDto(
    [property: JsonPropertyName("postal_code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PostalCode = null,
    [property: JsonPropertyName("ifns_ul"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IfnsUl = null,
    [property: JsonPropertyName("ifns_fl"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IfnsFl = null,
    [property: JsonPropertyName("okato"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Okato = null,
    [property: JsonPropertyName("oktmo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Oktmo = null,
    [property: JsonPropertyName("cadastral_number"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CadastralNumber = null,
    [property: JsonPropertyName("oktmo_budget"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OktmoBudget = null);

/// <summary>Федеральный округ (статический справочник, в данных ФИАС отсутствует).</summary>
public record FederalDistrictDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("short_name")] string ShortName,
    [property: JsonPropertyName("center_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CenterId = null);

/// <summary>Полный ответ для одного объекта адресации (формат ГАР).</summary>
public record AddressDto(
    [property: JsonPropertyName("object_id")] long ObjectId,
    [property: JsonPropertyName("object_level_id")] int ObjectLevelId,
    [property: JsonPropertyName("operation_type_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? OperationTypeId,
    [property: JsonPropertyName("object_guid")] Guid? ObjectGuid,
    [property: JsonPropertyName("address_type")] int AddressType,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("region_code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RegionCode,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path,
    [property: JsonPropertyName("address_details"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AddressDetailsDto? AddressDetails,
    [property: JsonPropertyName("hierarchy")] IReadOnlyList<AddressHierarchyItemDto> Hierarchy,
    [property: JsonPropertyName("federal_district"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FederalDistrictDto? FederalDistrict,
    [property: JsonPropertyName("hierarchy_place")] int HierarchyPlace);

/// <summary>Структурированные данные адреса для инлайн-выдачи (формат, близкий к DaData).</summary>
public record AddressDataDto(
    [property: JsonPropertyName("fias_id")] Guid? FiasId,
    [property: JsonPropertyName("fias_level"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? FiasLevel,
    [property: JsonPropertyName("region_code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RegionCode,
    [property: JsonPropertyName("region"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Region,
    [property: JsonPropertyName("area"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Area,
    [property: JsonPropertyName("city"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? City,
    [property: JsonPropertyName("settlement"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Settlement,
    [property: JsonPropertyName("street"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Street,
    [property: JsonPropertyName("house"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? House,
    [property: JsonPropertyName("postal_code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PostalCode,
    [property: JsonPropertyName("kladr_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? KladrId,
    [property: JsonPropertyName("okato"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Okato,
    [property: JsonPropertyName("oktmo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Oktmo,
    [property: JsonPropertyName("tax_office"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TaxOffice,        // ИФНС ФЛ
    [property: JsonPropertyName("tax_office_legal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TaxOfficeLegal); // ИФНС ЮЛ

/// <summary>Запись в результате поиска. <see cref="Data"/> — структурный разбор в стиле DaData.</summary>
public record AddressSearchResultDto(
    long ObjectId,
    Guid? ObjectGuid,
    int? Level,
    string? Name,
    string FullName,
    string Address,
    double Similarity,
    [property: JsonPropertyName("data"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AddressDataDto? Data = null);

/// <summary>Дочерний элемент иерархии.</summary>
public record AddressChildDto(
    long ObjectId,
    Guid? ObjectGuid,
    int Level,
    string FullName);

/// <summary>Элемент саджеста (формат, близкий к DaData): значение + структурный блок data.</summary>
public record SuggestionDto(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("unrestricted_value")] string UnrestrictedValue,
    [property: JsonPropertyName("data")] AddressDataDto Data);

/// <summary>Результат стандартизации строки: лучший разбор + код качества и уверенность.</summary>
public record CleanResultDto(
    [property: JsonPropertyName("value"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value,
    [property: JsonPropertyName("unrestricted_value"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UnrestrictedValue,
    [property: JsonPropertyName("data"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AddressDataDto? Data,
    // qc: 0 — распознано до дома; 1 — до улицы/города; 2 — нечётко; 3 — не распознано.
    [property: JsonPropertyName("qc")] int Qc,
    [property: JsonPropertyName("confidence")] double Confidence);
