namespace Fias.Application.Models;

/// <summary>Постраничный результат произвольного типа.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Краткая запись адресообразующего элемента (без полной адресной строки).</summary>
public record AddressBriefDto(
    long ObjectId,
    Guid? ObjectGuid,
    int Level,
    string? Type,
    string? ShortType,
    string? Name,
    string FullName);

/// <summary>Запись для GET /regions — субъект РФ.</summary>
public record RegionDto(long ObjectId, Guid? ObjectGuid, string Name, string? ShortType);

/// <summary>Уровень иерархии (OBJECT_LEVELS).</summary>
public record LevelDto(int Level, string? Name, string? ShortName);

/// <summary>Тип адресообразующего элемента или объекта адресации.</summary>
public record TypeDto(int Id, int? Level, string? Name, string? ShortName);

/// <summary>Запись о доме на улице.</summary>
public record HouseSummaryDto(
    long ObjectId,
    Guid? ObjectGuid,
    string? HouseNum,
    string? AddNum1,
    string? AddNum2,
    int? HouseType,
    string FullName);

/// <summary>Запись о помещении в доме.</summary>
public record ApartmentSummaryDto(
    long ObjectId,
    Guid? ObjectGuid,
    string? Number,
    int? ApartType,
    string FullName);

/// <summary>Запись о комнате в помещении.</summary>
public record RoomSummaryDto(
    long ObjectId,
    Guid? ObjectGuid,
    string? Number,
    int? RoomType,
    string FullName);
