using Dapper;
using Fias.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Fias.Api.Health;

/// <summary>
/// Готовность поисковой проекции. Схему и наполнение создаёт Updater (единый писатель),
/// поэтому Api не лезет в DDL, а лишь сообщает готовность для readiness-пробы:
///   • таблицы search.* нет → Unhealthy (схема не инициализирована);
///   • таблица пустая → Degraded (импорт ещё не выполнялся);
///   • есть данные → Healthy.
/// </summary>
public sealed class SearchProjectionHealthCheck(ISqlConnectionFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var conn = await factory.OpenAsync(ct);
            var count = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT count(*) FROM search.address_objects", cancellationToken: ct));

            return count > 0
                ? HealthCheckResult.Healthy($"search.address_objects: {count}")
                : HealthCheckResult.Degraded("Проекция пуста — импорт ещё не выполнялся");
        }
        catch (PostgresException ex) when (ex.SqlState is "42P01" or "3F000")
        {
            // 42P01 = undefined_table, 3F000 = invalid_schema_name.
            return HealthCheckResult.Unhealthy("Схема search.* не инициализирована — запустите импорт");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Ошибка проверки проекции", ex);
        }
    }
}
