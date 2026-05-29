using Fias.Application.Abstractions;
using Fias.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Fias.Application.Services;

public class ReferencesService(IFiasDbContext db) : IReferencesService
{
    public async Task<IReadOnlyList<LevelDto>> GetLevelsAsync(CancellationToken ct) =>
        await db.ObjectLevels.AsNoTracking()
            .Where(l => l.IsActive == true)
            .OrderBy(l => l.Level)
            .Select(l => new LevelDto(l.Level, l.Name, l.ShortName))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TypeDto>> GetAddressObjectTypesAsync(int? level, CancellationToken ct)
    {
        var q = db.AddressObjectTypes.AsNoTracking().Where(t => t.IsActive == true);
        if (level is { } lvl) q = q.Where(t => t.Level == lvl);
        return await q.OrderBy(t => t.Level).ThenBy(t => t.ShortName)
            .Select(t => new TypeDto(t.Id, t.Level, t.Name, t.ShortName))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TypeDto>> GetHouseTypesAsync(CancellationToken ct) =>
        await db.HouseTypes.AsNoTracking()
            .Where(t => t.IsActive == true)
            .OrderBy(t => t.Id)
            .Select(t => new TypeDto(t.Id, null, t.Name, t.ShortName))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TypeDto>> GetApartmentTypesAsync(CancellationToken ct) =>
        await db.ApartmentTypes.AsNoTracking()
            .Where(t => t.IsActive == true)
            .OrderBy(t => t.Id)
            .Select(t => new TypeDto(t.Id, null, t.Name, t.ShortName))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TypeDto>> GetRoomTypesAsync(CancellationToken ct) =>
        await db.RoomTypes.AsNoTracking()
            .Where(t => t.IsActive == true)
            .OrderBy(t => t.Id)
            .Select(t => new TypeDto(t.Id, null, t.Name, t.ShortName))
            .ToListAsync(ct);
}
