using Dapper;
using Fias.Application.Abstractions;
using Fias.Application.Models;

namespace Fias.Application.Services;

public class ServiceInfoService(ISqlConnectionFactory factory, IFiasVersionProvider version) : IServiceInfoService
{
    public Task<VersionDto> GetVersionAsync(CancellationToken ct) => version.GetAsync(ct);

    public async Task<StatsDto> GetStatsAsync(CancellationToken ct)
    {
        // Один запрос с подзапросами-счётчиками вместо пяти отдельных COUNT.
        const string sql = """
            SELECT
                (SELECT count(*) FROM fias.addressobjects WHERE isactual = true AND isactive = true)                AS "AddressObjects",
                (SELECT count(*) FROM fias.houses         WHERE isactual = true AND isactive = true)                AS "Houses",
                (SELECT count(*) FROM fias.apartments     WHERE isactual = true AND isactive = true)                AS "Apartments",
                (SELECT count(*) FROM fias.rooms          WHERE isactual = true AND isactive = true)                AS "Rooms",
                (SELECT count(*) FROM fias.addressobjects WHERE level = 1 AND isactual = true AND isactive = true)  AS "Regions"
            """;

        await using var conn = await factory.OpenAsync(ct);
        return await conn.QueryFirstAsync<StatsDto>(new CommandDefinition(sql, cancellationToken: ct));
    }
}
