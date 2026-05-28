using Fias.Service.Updater.Services.Db;
using Fias.Service.Updater.Services.Downloading;
using Npgsql;

namespace Fias.Service.Updater.Services.State;

public interface IFiasVersionStore
{
    Task<int?> GetCurrentVersionAsync(CancellationToken ct);
    Task SetVersionAsync(DownloadFileInfo info, CancellationToken ct);
}

public class FiasVersionStore(INpgsqlConnectionFactory factory) : IFiasVersionStore
{
    public async Task<int?> GetCurrentVersionAsync(CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT version_id FROM fias.import_state WHERE id = 1", conn);
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw is null or DBNull ? null : Convert.ToInt32(raw);
    }

    public async Task SetVersionAsync(DownloadFileInfo info, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO fias.import_state (id, version_id, text_version, applied_at)
            VALUES (1, @v, @t, now())
            ON CONFLICT (id) DO UPDATE
                SET version_id = excluded.version_id,
                    text_version = excluded.text_version,
                    applied_at = excluded.applied_at
            """, conn);
        cmd.Parameters.AddWithValue("v", info.VersionId);
        cmd.Parameters.AddWithValue("t", info.TextVersion);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
