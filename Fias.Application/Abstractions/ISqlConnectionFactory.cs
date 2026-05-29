using System.Data.Common;

namespace Fias.Application.Abstractions;

/// <summary>
/// Открывает соединение с БД для Dapper-запросов в use case'ах. Реализация — в Infrastructure
/// поверх NpgsqlDataSource. Пришла на смену IFiasDbContext после отказа от EF в ядре чтения.
/// </summary>
public interface ISqlConnectionFactory
{
    /// <summary>Открыть новое соединение из пула. Вызывающий обязан его освободить (await using).</summary>
    Task<DbConnection> OpenAsync(CancellationToken ct);
}
