using Fias.Application.Abstractions;
using Fias.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Fias.Application.Services;

public class ServiceInfoService(IFiasDbContext db, IFiasVersionProvider version) : IServiceInfoService
{
    public Task<VersionDto> GetVersionAsync(CancellationToken ct) => version.GetAsync(ct);

    public async Task<StatsDto> GetStatsAsync(CancellationToken ct)
    {
        // Подсчёт активных записей. EF Core транслирует Count() в COUNT(*).
        var addressObjects = await db.AddressObjects.AsNoTracking()
            .CountAsync(a => a.IsActual == true && a.IsActive == true, ct);
        var houses = await db.Houses.AsNoTracking()
            .CountAsync(h => h.IsActual == true && h.IsActive == true, ct);
        var apartments = await db.Apartments.AsNoTracking()
            .CountAsync(a => a.IsActual == true && a.IsActive == true, ct);
        var rooms = await db.Rooms.AsNoTracking()
            .CountAsync(r => r.IsActual == true && r.IsActive == true, ct);
        var regions = await db.AddressObjects.AsNoTracking()
            .CountAsync(a => a.Level == 1 && a.IsActual == true && a.IsActive == true, ct);

        return new StatsDto(addressObjects, houses, apartments, rooms, regions);
    }
}
