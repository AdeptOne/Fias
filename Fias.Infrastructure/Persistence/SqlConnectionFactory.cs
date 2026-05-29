using System.Data.Common;
using Fias.Application.Abstractions;
using Npgsql;

namespace Fias.Infrastructure.Persistence;

/// <summary>Реализация <see cref="ISqlConnectionFactory"/> поверх пула NpgsqlDataSource.</summary>
public sealed class SqlConnectionFactory(NpgsqlDataSource dataSource) : ISqlConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken ct) =>
        await dataSource.OpenConnectionAsync(ct);
}
