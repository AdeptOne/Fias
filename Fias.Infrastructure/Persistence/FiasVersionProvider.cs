using Fias.Application.Abstractions;
using Fias.Application.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fias.Infrastructure.Persistence;

public class FiasVersionProvider(FiasDbContext db) : IFiasVersionProvider
{
    public async Task<VersionDto> GetAsync(CancellationToken ct)
    {
        // Таблица fias.import_state живёт в схеме, которую создаёт Updater. Если схема ещё не применена
        // (первый запуск), отдаём пустую версию вместо 500.
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await db.Database.OpenConnectionAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT version_id, text_version, applied_at FROM fias.import_state WHERE id = 1";
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct))
                return new VersionDto(null, null, null);

            return new VersionDto(
                reader.IsDBNull(0) ? null : reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2));
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01" || ex.SqlState == "3F000")
        {
            // 42P01 = undefined_table, 3F000 = invalid_schema_name.
            return new VersionDto(null, null, null);
        }
    }
}
