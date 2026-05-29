using Fias.Application.Abstractions;
using Fias.Application.Services;
using Fias.Infrastructure.Hangfire;
using Fias.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Fias.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default не задан");

        // Единый пул соединений на приложение. Всё чтение API идёт через Dapper поверх него
        // (EF убран). NpgsqlDataSource — потокобезопасный синглтон.
        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();

        services.AddScoped<IAddressSearchRepository, AddressSearchRepository>();
        services.AddScoped<IFiasVersionProvider, FiasVersionProvider>();
        services.AddScoped<IAdminImportService, AdminImportService>();

        return services;
    }
}
