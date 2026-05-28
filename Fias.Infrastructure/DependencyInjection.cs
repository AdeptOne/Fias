using Fias.Application.Abstractions;
using Fias.Application.Services;
using Fias.Infrastructure.Hangfire;
using Fias.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fias.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default не задан");

        services.AddDbContext<FiasDbContext>(opt =>
        {
            opt.UseNpgsql(connectionString);
            opt.UseLowerCaseNamingConvention();
        });

        services.AddScoped<IFiasDbContext>(sp => sp.GetRequiredService<FiasDbContext>());
        services.AddScoped<IAddressSearchRepository, AddressSearchRepository>();
        services.AddScoped<IFiasVersionProvider, FiasVersionProvider>();
        services.AddScoped<IAdminImportService, AdminImportService>();

        return services;
    }
}
