using Fias.Application.Models;

namespace Fias.Application.Services;

public interface IAddressBuilderService
{
    /// <summary>Построить адресную строку и иерархию по OBJECTID. null — объект не найден или неактивен.</summary>
    Task<AddressDto?> BuildByObjectIdAsync(long objectId, CancellationToken ct);

    /// <summary>Аналогично по OBJECTGUID активного адресного объекта.</summary>
    Task<AddressDto?> BuildByObjectGuidAsync(Guid objectGuid, CancellationToken ct);

    /// <summary>Построить адресную строку для пути (PATH из adm_hierarchy).</summary>
    Task<AddressDto?> BuildByPathAsync(long leafObjectId, string path, CancellationToken ct);
}
