using Fias.Application.Models;

namespace Fias.Application.Services;

public interface IReferencesService
{
    Task<IReadOnlyList<LevelDto>> GetLevelsAsync(CancellationToken ct);
    Task<IReadOnlyList<TypeDto>> GetAddressObjectTypesAsync(int? level, CancellationToken ct);
    Task<IReadOnlyList<TypeDto>> GetHouseTypesAsync(CancellationToken ct);
    Task<IReadOnlyList<TypeDto>> GetApartmentTypesAsync(CancellationToken ct);
    Task<IReadOnlyList<TypeDto>> GetRoomTypesAsync(CancellationToken ct);
}
