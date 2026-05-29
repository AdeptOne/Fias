namespace Fias.Application.Search;

/// <summary>
/// Сырая строка результата гибридного поиска (до построения полной адресной строки).
/// Возвращается репозиторием. Несёт обе «оценки» отдельно (<see cref="FtsRank"/> и
/// <see cref="TrgmSimilarity"/>) для отладки/трассировки ранжирования плюс итоговый
/// <see cref="Score"/>, по которому уже отсортированы строки.
/// </summary>
public sealed record AddressResult(
    long ObjectId,
    Guid? ObjectGuid,
    /// <summary>GUID родительского контейнера (города/улицы), внутри которого нашли объект; null для верхнего уровня.</summary>
    Guid? ParentGuid,
    int? Level,
    string? Name,
    string? TypeName,
    /// <summary>Денормализованная полная адресная строка (из проекции search.*); для отображения без JOIN.</summary>
    string? FullName,
    int? RegionCode,
    string? PostalCode,
    string? Okato,
    string? Oktmo,
    string? IfnsUl,
    string? IfnsFl,
    string? KladrCode,
    string? Region,
    string? Area,
    string? City,
    string? Settlement,
    string? Street,
    /// <summary>ts_rank полнотекстового совпадения; 0, если совпадения по FTS не было.</summary>
    double FtsRank,
    /// <summary>pg_trgm similarity (0..1) — мера «похожести» для опечаток.</summary>
    double TrgmSimilarity,
    /// <summary>Итоговый комбинированный скор ранжирования (FTS строго важнее триграмм + бонус уровню).</summary>
    double Score);
