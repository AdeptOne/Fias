using Dapper;
using Fias.Application.Abstractions;
using Fias.Application.Models;
using Npgsql;

namespace Fias.Infrastructure.Persistence;

public class FiasVersionProvider(ISqlConnectionFactory factory) : IFiasVersionProvider
{
    public async Task<VersionDto> GetAsync(CancellationToken ct)
    {
        // Таблица fias.import_state создаётся Updater'ом. Если схема ещё не применена
        // (первый запуск) — отдаём пустую версию вместо 500.
        try
        {
            await using var conn = await factory.OpenAsync(ct);
            var row = await conn.QueryFirstOrDefaultAsync<VersionRow>(new CommandDefinition(
                """
                SELECT version_id AS "VersionId", text_version AS "TextVersion", applied_at AS "AppliedAt"
                FROM fias.import_state WHERE id = 1
                """,
                cancellationToken: ct));

            return row is null
                ? new VersionDto(null, null, null)
                : new VersionDto(row.VersionId, row.TextVersion, row.AppliedAt);
        }
        catch (PostgresException ex) when (ex.SqlState is "42P01" or "3F000")
        {
            // 42P01 = undefined_table, 3F000 = invalid_schema_name.
            return new VersionDto(null, null, null);
        }
    }

    private sealed record VersionRow(int? VersionId, string? TextVersion, DateTime? AppliedAt);
}
