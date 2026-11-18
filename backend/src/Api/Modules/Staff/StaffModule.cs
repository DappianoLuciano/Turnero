using Api.Modules.Staff.Contracts;
using Api.Modules.Staff.Features;
using Api.Shared.Hosting;

namespace Api.Modules.Staff;

/// <summary>Professionals and the services each one performs.</summary>
internal static class StaffModule
{
    public static IServiceCollection AddStaffModule(this IServiceCollection services) =>
        services.AddScoped<IProfessionalDirectory, ProfessionalDirectory>();

    public static void MapStaffEndpoints(ApiRouteGroups groups)
    {
        ListProfessionals.Map(groups.Tenant);
        CreateProfessional.Map(groups.Tenant);
        UpdateProfessional.Map(groups.Tenant);
        DeleteProfessional.Map(groups.Tenant);
    }
}
