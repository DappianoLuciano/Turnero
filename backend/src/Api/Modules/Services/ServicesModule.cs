using Api.Modules.Services.Contracts;
using Api.Modules.Services.Features;
using Api.Shared.Hosting;

namespace Api.Modules.Services;

/// <summary>Catalog of services: name, duration, price.</summary>
internal static class ServicesModule
{
    public static IServiceCollection AddServicesModule(this IServiceCollection services) =>
        services.AddScoped<IServiceCatalog, ServiceCatalog>();

    public static void MapServicesEndpoints(ApiRouteGroups groups)
    {
        ListServices.Map(groups.Tenant);
        CreateService.Map(groups.Tenant);
        UpdateService.Map(groups.Tenant);
        DeleteService.Map(groups.Tenant);
    }
}
