using Fias.Application.Models;

namespace Fias.Application.Services;

public interface IAddressSearchService
{
    /// <summary>Поиск по строке (адрес или FIAS GUID). Без настроек — порог/уровни внутри.</summary>
    /// <param name="query">Строка: адрес или FIAS GUID.</param>
    /// <param name="limit">Максимум результатов.</param>
    Task<IReadOnlyList<AddressSearchResultDto>> SearchAsync(string query, int limit, CancellationToken ct);

    /// <summary>Поиск с полной структурой ГАР по каждому найденному объекту.</summary>
    Task<IReadOnlyList<AddressDto>> SearchAddressesAsync(string query, int limit, CancellationToken ct);

    /// <summary>Дочерние элементы по OBJECTID родителя (адм. деление), опционально по уровню.</summary>
    Task<IReadOnlyList<AddressChildDto>> GetChildrenAsync(
        long objectId, int? level, string? nameFilter, int page, int pageSize, CancellationToken ct);

    /// <summary>Путь от объекта к корню (для breadcrumbs). Возвращает иерархию от корня к листу.</summary>
    Task<IReadOnlyList<AddressHierarchyItemDto>> GetParentsAsync(long objectId, CancellationToken ct);

    /// <summary>Саджест в формате DaData (value/unrestricted_value/data). Принимает адрес или
    /// FIAS GUID — если строка является GUID, вернёт один объект напрямую.</summary>
    Task<IReadOnlyList<SuggestionDto>> SuggestAsync(string query, int count, CancellationToken ct);

    /// <summary>Резолв одного объекта по FIAS GUID в формат саджеста.</summary>
    Task<SuggestionDto?> SuggestByGuidAsync(Guid fiasId, CancellationToken ct);

    /// <summary>Стандартизация: сырая строка → один лучший разобранный адрес + qc/confidence.</summary>
    Task<CleanResultDto> CleanAsync(string query, CancellationToken ct);
}
