using Fias.Application.Models;

namespace Fias.Application.Services;

public interface IHierarchyService
{
    /// <summary>Все субъекты РФ (LEVEL=1), активные.</summary>
    Task<IReadOnlyList<RegionDto>> GetRegionsAsync(CancellationToken ct);

    /// <summary>Дома на улице (level 8 → дочерние level 10).</summary>
    Task<PagedResult<HouseSummaryDto>> GetHousesByStreetAsync(
        long streetObjectId, string? numFilter, int page, int pageSize, CancellationToken ct);

    /// <summary>Помещения в доме.</summary>
    Task<PagedResult<ApartmentSummaryDto>> GetApartmentsByHouseAsync(
        long houseObjectId, string? numFilter, int page, int pageSize, CancellationToken ct);

    /// <summary>Комнаты в помещении.</summary>
    Task<PagedResult<RoomSummaryDto>> GetRoomsByApartmentAsync(
        long apartmentObjectId, int page, int pageSize, CancellationToken ct);
}
