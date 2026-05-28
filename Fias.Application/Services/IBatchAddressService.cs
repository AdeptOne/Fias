using Fias.Application.Models;

namespace Fias.Application.Services;

public interface IBatchAddressService
{
    /// <summary>Возвращает адресные строки для списка OBJECTID одним запросом (до 1000).</summary>
    Task<BatchAddressResponse> ResolveAsync(BatchAddressRequest request, CancellationToken ct);
}
