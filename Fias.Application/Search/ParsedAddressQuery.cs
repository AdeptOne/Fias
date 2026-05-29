namespace Fias.Application.Search;

/// <summary>
/// Результат препроцессинга пользовательской строки: нормализованный текст,
/// токены и эвристическое разбиение на «контейнер» (регион/город), улицу и дом.
///
/// Текстовые поля (<see cref="Normalized"/>, <see cref="RegionOrCity"/>, <see cref="Street"/>,
/// <see cref="House"/>) заполняет <c>AddressNormalizer</c>; параметры запроса
/// (<see cref="Limit"/>, <see cref="SimilarityThreshold"/>, <see cref="LevelFilter"/>,
/// <see cref="ParentObjectId"/>) досыпает вызывающий сервис через <c>with { ... }</c>.
/// Record с init-свойствами — иммутабельный value-объект, удобный для проброса в репозиторий.
/// </summary>
public sealed record ParsedAddressQuery
{
    /// <summary>Исходная строка пользователя, как пришла.</summary>
    public required string Raw { get; init; }

    /// <summary>Очищенная и нормализованная строка (нижний регистр, ё→е, схлопнутые пробелы).</summary>
    public required string Normalized { get; init; }

    /// <summary>Токены нормализованной строки с раскрытыми сокращениями (нск→новосибирск).</summary>
    public required IReadOnlyList<string> Tokens { get; init; }

    /// <summary>Потенциальный регион/город — «контейнер» верхнего уровня. null, если не вычленён.</summary>
    public string? RegionOrCity { get; init; }

    /// <summary>Потенциальная улица (без типа «ул»). null, если не вычленена.</summary>
    public string? Street { get; init; }

    /// <summary>Номер дома вместе с корпусом/строением в нормализованном виде («12к1»). null, если нет.
    /// Используется как fallback-термин для триграммного поиска и для логов.</summary>
    public string? House { get; init; }

    /// <summary>Базовый номер дома без корпуса/строения («12», «12а»). Матчится против houses.housenum.</summary>
    public string? HouseNum { get; init; }

    /// <summary>Номер корпуса/строения («1»). Матчится против houses.addnum1 / addnum2.</summary>
    public string? Building { get; init; }

    // --- Параметры запроса (заполняются сервисом, не нормализатором) --------

    /// <summary>Максимум результатов.</summary>
    public int Limit { get; init; } = 10;

    /// <summary>Порог pg_trgm similarity для триграммного фолбэка (0.1..1.0).</summary>
    public double SimilarityThreshold { get; init; } = 0.3;

    /// <summary>Опциональный фильтр по точному уровню (legacy /search). Имеет приоритет над диапазоном.</summary>
    public int? LevelFilter { get; init; }

    /// <summary>Нижняя граница уровня (broad, напр. город) — из from_bound саджеста.</summary>
    public int? LevelFrom { get; init; }

    /// <summary>Верхняя граница уровня (narrow, напр. улица) — из to_bound саджеста.</summary>
    public int? LevelTo { get; init; }

    /// <summary>Ограничение области поиска кодом субъекта РФ (locations у DaData).</summary>
    public int? RegionCode { get; init; }

    /// <summary>Опциональное ограничение поиска поддеревом заданного OBJECTID.</summary>
    public long? ParentObjectId { get; init; }

    /// <summary>true, если в строке нашёлся хоть один значимый сегмент для поиска.</summary>
    public bool HasContent => RegionOrCity is not null || Street is not null || House is not null
                              || Normalized.Length >= 2;
}
