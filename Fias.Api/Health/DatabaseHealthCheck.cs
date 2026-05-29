using Dapper;
using Fias.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fias.Api.Health;

/// <summary>Проверка связи с БД: открыть соединение и выполнить SELECT 1.</summary>
public sealed class DatabaseHealthCheck(ISqlConnectionFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var conn = await factory.OpenAsync(ct);
            await conn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: ct));
            return HealthCheckResult.Healthy("БД доступна");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Нет связи с БД", ex);
        }
    }
}
