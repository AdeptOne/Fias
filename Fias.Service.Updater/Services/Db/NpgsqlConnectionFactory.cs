using Npgsql;

namespace Fias.Service.Updater.Services.Db;

public interface INpgsqlConnectionFactory
{
    Task<NpgsqlConnection> OpenAsync(CancellationToken ct);
    string ConnectionString { get; }
}

public class NpgsqlConnectionFactory(IConfiguration configuration) : INpgsqlConnectionFactory
{
    public string ConnectionString { get; } =
        configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default не задан");

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
