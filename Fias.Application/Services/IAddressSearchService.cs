using Fias.Application.Models;

namespace Fias.Application.Services;

public interface IAddressSearchService
{
    /// <summary>Нечёткий поиск по наименованию через pg_trgm (similarity).</summary>
    Task<IReadOnlyList<AddressSearchResultDto>> SearchAsync(
        string query, int limit, double threshold, CancellationToken ct);

    /// <summary>Дочерние элементы по OBJECTID родителя (адм. деление).</summary>
    Task<IReadOnlyList<AddressChildDto>> GetChildrenAsync(long objectId, CancellationToken ct);
}
