using Fias.Application.Abstractions;
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

        // Application запрашивает IFiasDbContext — отдаём ему уже зарегистрированный FiasDbContext.
        services.AddScoped<IFiasDbContext>(sp => sp.GetRequiredService<FiasDbContext>());
        services.AddScoped<IAddressSearchRepository, AddressSearchRepository>();

        return services;
    }
}
