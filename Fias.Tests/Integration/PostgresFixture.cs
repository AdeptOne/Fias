using Fias.Service.Updater.Services.Db;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace Fias.Tests.Integration;

/// <summary>
/// Поднимает одноразовый Postgres 16 в Docker (Testcontainers) для интеграционных тестов
/// всего SQL-конвейера. Запускается ТОЛЬКО там, где доступен Docker (CI/локально);
/// в sandbox без Docker тесты этой категории пропускаются.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("fias")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public INpgsqlConnectionFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = ConnectionString })
            .Build();
        Factory = new NpgsqlConnectionFactory(config);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
