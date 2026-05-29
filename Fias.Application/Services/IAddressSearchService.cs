using Fias.Application.Models;

namespace Fias.Application.Services;

public interface IAddressSearchService
{
    /// <summary>Нечёткий поиск по наименованию через pg_trgm (similarity).</summary>
    /// <param name="query">Строка поиска (минимум 2 символа).</param>
    /// <param name="limit">Максимум результатов, 1..100.</param>
    /// <param name="threshold">Порог similarity, 0.1..1.0.</param>
    /// <param name="level">Опциональный фильтр по уровню адресообразующего элемента.</param>
    /// <param name="parentId">Опциональный фильтр: искать только потомков заданного OBJECTID.</param>
    Task<IReadOnlyList<AddressSearchResultDto>> SearchAsync(
        string query, int limit, double threshold, int? level, long? parentId, CancellationToken ct);

    /// <summary>Поиск с полной структурой ГАР по каждому найденному объекту: { "addresses": [...] }.</summary>
    Task<AddressListResponse> SearchAddressesAsync(
        string query, int limit, double threshold, int? level, long? parentId, CancellationToken ct);

    /// <summary>Дочерние элементы по OBJECTID родителя (адм. деление), опционально по уровню.</summary>
    Task<IReadOnlyList<AddressChildDto>> GetChildrenAsync(
        long objectId, int? level, string? nameFilter, int page, int pageSize, CancellationToken ct);

    /// <summary>Путь от объекта к корню (для breadcrumbs). Возвращает иерархию от корня к листу.</summary>
    Task<IReadOnlyList<AddressHierarchyItemDto>> GetParentsAsync(long objectId, CancellationToken ct);

    /// <summary>Саджест адреса в формате, близком к DaData (value/unrestricted_value/data).</summary>
    /// <param name="fromBound">Нижняя граница уровня: region|area|city|settlement|street|house.</param>
    /// <param name="toBound">Верхняя граница уровня (тех же значений).</param>
    /// <param name="regionCode">Ограничение области поиска кодом субъекта РФ.</param>
    /// <param name="parentId">Ограничение поддеревом OBJECTID.</param>
    Task<SuggestionsResponse> SuggestAsync(
        string query, int count, string? fromBound, string? toBound, int? regionCode, long? parentId, CancellationToken ct);

    /// <summary>Резолв одного объекта по FIAS GUID в формат саджеста.</summary>
    Task<SuggestionDto?> SuggestByGuidAsync(Guid fiasId, CancellationToken ct);

    /// <summary>Стандартизация: сырая строка → один лучший разобранный адрес + qc/confidence.</summary>
    Task<CleanResultDto> CleanAsync(string query, CancellationToken ct);
}
