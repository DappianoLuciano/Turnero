using Api.Modules.Booking.Features;
using Api.Shared.Hosting;

namespace Api.Modules.Booking;

/// <summary>
/// Public booking site (reservas): catalog, availability and booking without a customer account. Owns no tables:
/// it composes the other modules through their contracts. Endpoints live under <c>/api/public/{slug}</c>.
/// </summary>
internal static class BookingModule
{
    public static IServiceCollection AddBookingModule(this IServiceCollection services) =>
        services.AddScoped<PublicAppointments>();

    public static void MapBookingEndpoints(ApiRouteGroups groups)
    {
        GetPublicCatalog.Map(groups.Public);
        GetPublicAvailability.Map(groups.Public);
        CreatePublicBooking.Map(groups.Public);
        GetPublicAppointment.Map(groups.Public);
        GetPublicAppointmentAvailability.Map(groups.Public);
        ReschedulePublicAppointment.Map(groups.Public);
        CancelPublicAppointment.Map(groups.Public);
    }
}
