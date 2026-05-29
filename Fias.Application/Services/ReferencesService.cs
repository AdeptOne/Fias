using Dapper;
using Fias.Application.Abstractions;
using Fias.Application.Models;

namespace Fias.Application.Services;

public class ReferencesService(ISqlConnectionFactory factory) : IReferencesService
{
    // Колонки fias.* без подчёркиваний (shortname/levelid) — Dapper матчит их на свойства
    // DTO регистронезависимо, поэтому алиасы не нужны.
    public async Task<IReadOnlyList<LevelDto>> GetLevelsAsync(CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<LevelDto>(new CommandDefinition(
            "SELECT level, name, shortname FROM fias.object_levels WHERE isactive = true ORDER BY level",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<TypeDto>> GetAddressObjectTypesAsync(int? level, CancellationToken ct)
    {
        const string sql = """
            SELECT id, level, name, shortname
            FROM fias.addressobject_types
            WHERE isactive = true AND (@level::int IS NULL OR level = @level)
            ORDER BY level, shortname
            """;
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<TypeDto>(new CommandDefinition(sql, new { level }, cancellationToken: ct));
        return rows.AsList();
    }

    public Task<IReadOnlyList<TypeDto>> GetHouseTypesAsync(CancellationToken ct) => SimpleTypesAsync("fias.house_types", ct);
    public Task<IReadOnlyList<TypeDto>> GetApartmentTypesAsync(CancellationToken ct) => SimpleTypesAsync("fias.apartment_types", ct);
    public Task<IReadOnlyList<TypeDto>> GetRoomTypesAsync(CancellationToken ct) => SimpleTypesAsync("fias.room_types", ct);

    /// <summary>Справочники без уровня (дома/помещения/комнаты): level отдаём как NULL.</summary>
    private async Task<IReadOnlyList<TypeDto>> SimpleTypesAsync(string table, CancellationToken ct)
    {
        var sql = $"SELECT id, NULL::int AS level, name, shortname FROM {table} WHERE isactive = true ORDER BY id";
        await using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<TypeDto>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }
}
