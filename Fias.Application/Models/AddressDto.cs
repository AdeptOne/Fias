namespace Fias.Application.Models;

/// <summary>Один уровень иерархии в адресной строке.</summary>
public record AddressHierarchyItemDto(
    long ObjectId,
    Guid? ObjectGuid,
    int Level,
    string? Type,
    string? ShortType,
    string? Name,
    string FullName);

/// <summary>Полный ответ для одного объекта адресации.</summary>
public record AddressDto(
    long ObjectId,
    Guid? ObjectGuid,
    int Level,
    string Address,
    string ShortAddress,
    IReadOnlyList<AddressHierarchyItemDto> Hierarchy);

/// <summary>Запись в результате поиска.</summary>
public record AddressSearchResultDto(
    long ObjectId,
    Guid? ObjectGuid,
    int? Level,
    string? Name,
    string FullName,
    string Address,
    double Similarity);

/// <summary>Дочерний элемент иерархии.</summary>
public record AddressChildDto(
    long ObjectId,
    Guid? ObjectGuid,
    int Level,
    string FullName);
