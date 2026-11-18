using Api.Modules.Schedules.Features;
using Api.Shared.Hosting;

namespace Api.Modules.Schedules;

/// <summary>Schedules (horarios): weekly working hours per professional, closed days, and availability.</summary>
internal static class SchedulesModule
{
    public static IServiceCollection AddSchedulesModule(this IServiceCollection services) => services;

    public static void MapSchedulesEndpoints(ApiRouteGroups groups)
    {
        GetSchedules.Map(groups.Tenant);
        ReplaceProfessionalSchedule.Map(groups.Tenant);
        ListScheduleExceptions.Map(groups.Tenant);
        CreateScheduleException.Map(groups.Tenant);
        DeleteScheduleException.Map(groups.Tenant);
        GetAvailability.Map(groups.Tenant);
    }
}
