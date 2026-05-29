using Fias.Application.Search;

namespace Fias.Application.Abstractions;

/// <summary>
/// Репозиторий гибридного поиска адресов. Скрывает за собой сырой SQL (Dapper) и
/// провайдер-специфику PostgreSQL: полнотекстовый поиск (tsvector/ts_rank) + триграммы
/// (pg_trgm/similarity), а также поуровневое сужение по дереву adm_hierarchy.
/// </summary>
public interface IAddressSearchRepository
{
    /// <summary>
    /// Выполнить гибридный поиск по разобранному запросу. Внутри — двухфазная логика:
    /// сначала резолвится контейнер (регион/город), затем улица/дом ищутся строго в его
    /// поддереве, чтобы не сканировать всю базу ГАР.
    /// </summary>
    Task<IReadOnlyList<AddressResult>> SearchAsync(ParsedAddressQuery query, CancellationToken ct);
}
