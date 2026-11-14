using Api.Modules.Businesses.Contracts;
using Api.Modules.Businesses.Features;
using Api.Shared.Hosting;

namespace Api.Modules.Businesses;

/// <summary>Businesses (tenants): profile, slug, time zone, currency.</summary>
internal static class BusinessesModule
{
    public static IServiceCollection AddBusinessesModule(this IServiceCollection services)
    {
        services.AddScoped<IBusinessDirectory, BusinessDirectory>();
        services.AddScoped<IBusinessProvisioning, BusinessProvisioning>();
        return services;
    }

    public static void MapBusinessesEndpoints(ApiRouteGroups groups)
    {
        GetCurrentBusiness.Map(groups.Tenant);
    }
}
