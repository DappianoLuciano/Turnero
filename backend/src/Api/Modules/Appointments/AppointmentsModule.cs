using Api.Modules.Appointments.Contracts;
using Api.Modules.Appointments.Features;
using Api.Shared.Hosting;

namespace Api.Modules.Appointments;

/// <summary>Appointments (agenda): who, with whom, for which service and when.</summary>
internal static class AppointmentsModule
{
    public static IServiceCollection AddAppointmentsModule(this IServiceCollection services) =>
        services.AddScoped<AppointmentBooking>().AddScoped<AppointmentViews>()
            .AddScoped<IAppointmentLookup, AppointmentLookup>()
            .AddScoped<IAppointmentStatistics, AppointmentStatistics>()
            .AddScoped<IAppointmentOccupancy, AppointmentOccupancy>()
            .AddScoped<IAppointmentCreator, AppointmentCreator>()
            .AddScoped<IAppointmentLinks, AppointmentLinks>()
            .AddScoped<IAppointmentSelfService, AppointmentSelfService>();

    public static void MapAppointmentsEndpoints(ApiRouteGroups groups)
    {
        ListAppointments.Map(groups.Tenant);
        ListReminders.Map(groups.Tenant);
        MarkReminderSent.Map(groups.Tenant);
        CreateAppointment.Map(groups.Tenant);
        UpdateAppointment.Map(groups.Tenant);
        ChangeAppointmentStatus.Map(groups.Tenant);
    }
}
