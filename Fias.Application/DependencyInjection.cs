using Fias.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Fias.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAddressBuilderService, AddressBuilderService>();
        services.AddScoped<IAddressSearchService, AddressSearchService>();
        services.AddScoped<IHierarchyService, HierarchyService>();
        services.AddScoped<IReferencesService, ReferencesService>();
        services.AddScoped<IServiceInfoService, ServiceInfoService>();
        return services;
    }
}
