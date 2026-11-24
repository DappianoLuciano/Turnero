using Api.Modules.Appointments.Contracts;
using Api.Modules.Businesses.Contracts;
using Api.Modules.Customers.Contracts;
using Api.Modules.Schedules.Contracts;
using Api.Modules.Services.Contracts;
using Api.Modules.Staff.Contracts;
using Api.Shared.Errors;
using Api.Shared.Tenancy;
using Api.Shared.Time;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Modules.Booking.Features;

// PUBLIC (no login): the customer reaches their own appointment through the secret link they were given
// (/r/{slug}/turno/{token}). The token is a signature, so an unknown or foreign token is just a 404.

/// <param name="CanChange">True while the appointment is scheduled and still outside the business's notice window.</param>
/// <param name="ChangeDeadline">Last moment the customer can change it on their own; null if it is not scheduled.</param>
public sealed record PublicAppointmentResponse(
    string BusinessName,
    string? BusinessPhone,
    Guid ProfessionalId,
    string ProfessionalName,
    Guid ServiceId,
    string ServiceName,
    string CustomerName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal Price,
    string Currency,
    string Status,
    bool CanChange,
    DateTimeOffset? ChangeDeadline,
    int NoticeHours);

/// <summary>Loads and presents the appointment a token points to, and applies the business's notice policy.</summary>
internal sealed class PublicAppointments(
    IBusinessDirectory businesses, IAppointmentLinks links, IProfessionalDirectory professionals,
    IServiceCatalog catalog, ICustomerDirectory customers, TimeProvider clock)
{
    private const string Scheduled = "Scheduled";

    public async Task<(ManagedAppointment Appointment, BusinessSummary Business)> LoadAsync(
        string token, CancellationToken cancellationToken)
    {
        var business = await businesses.GetCurrentAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");
        var appointment = await links.FindByTokenAsync(token, cancellationToken)
            ?? throw AppException.NotFound("appointment_not_found", "Appointment not found.");
        return (appointment, business);
    }

    private static DateTimeOffset? DeadlineOf(ManagedAppointment appointment, BusinessSummary business) =>
        appointment.Status == Scheduled ? appointment.StartsAt.AddHours(-business.CustomerChangeNoticeHours) : null;

    /// <summary>409 unless the customer may still change this appointment on their own.</summary>
    public void EnsureChangeable(ManagedAppointment appointment, BusinessSummary business)
    {
        if (appointment.Status != Scheduled)
        {
            throw AppException.Conflict("appointment_not_editable", "Only scheduled appointments can be changed.");
        }

        if (clock.GetUtcNow() > DeadlineOf(appointment, business))
        {
            throw AppException.Conflict("change_window_closed",
                "It is too late to change this appointment online. Contact the business.");
        }
    }

    public async Task<PublicAppointmentResponse> BuildAsync(
        ManagedAppointment a, BusinessSummary business, CancellationToken cancellationToken)
    {
        var professional = (await professionals.GetByIdsAsync([a.ProfessionalId], cancellationToken)).SingleOrDefault();
        var service = (await catalog.GetByIdsAsync([a.ServiceId], cancellationToken)).SingleOrDefault();
        var customer = (await customers.GetByIdsAsync([a.CustomerId], cancellationToken)).SingleOrDefault();
        var deadline = DeadlineOf(a, business);
        return new PublicAppointmentResponse(
            business.Name, business.Phone, a.ProfessionalId, professional?.Name ?? string.Empty, a.ServiceId,
            service?.Name ?? string.Empty, customer?.Name ?? string.Empty, a.StartsAt, a.EndsAt, a.Price, a.Currency,
            a.Status, deadline is not null && clock.GetUtcNow() <= deadline, deadline, business.CustomerChangeNoticeHours);
    }
}

/// <summary>GET /api/public/{slug}/appointments/{token}: the customer's appointment and whether they can still change it.</summary>
internal static class GetPublicAppointment
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/appointments/{token}", HandleAsync)
            .WithName("GetPublicAppointment")
            .WithTags("PublicBooking")
            .Produces<PublicAppointmentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        [FromRoute] string slug, [FromRoute] string token, PublicAppointments appointments,
        CancellationToken cancellationToken)
    {
        _ = slug; // Resolved to the tenant by PublicTenantFilter; declared so it appears in the OpenAPI contract.
        var (appointment, business) = await appointments.LoadAsync(token, cancellationToken);
        return TypedResults.Ok(await appointments.BuildAsync(appointment, business, cancellationToken));
    }
}

/// <summary>GET /api/public/{slug}/appointments/{token}/availability?date=: free times to move that appointment to.</summary>
internal static class GetPublicAppointmentAvailability
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/appointments/{token}/availability", HandleAsync)
            .WithName("GetPublicAppointmentAvailability")
            .WithTags("PublicBooking")
            .Produces<PublicAvailabilityResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        [FromRoute] string slug, [FromRoute] string token, DateOnly date, PublicAppointments appointments,
        IAvailabilityService availability, TimeProvider clock, CancellationToken cancellationToken)
    {
        _ = slug; // Resolved to the tenant by PublicTenantFilter; declared so it appears in the OpenAPI contract.
        var (appointment, business) = await appointments.LoadAsync(token, cancellationToken);
        appointments.EnsureChangeable(appointment, business);
        BookingRules.EnsureBookableDate(date, business.TimeZone, clock);

        // The appointment's own time is free for itself, so it can be moved a little (e.g. 15 minutes later).
        var result = await availability.GetAsync(
            appointment.ProfessionalId, appointment.ServiceId, date, appointment.Id, cancellationToken);
        return TypedResults.Ok(new PublicAvailabilityResponse(
            result.Date, result.IsClosed, result.Slots.Select(s => new PublicSlotResponse(s.StartsAt, s.Minutes)).ToList()));
    }
}

/// <summary>POST /api/public/{slug}/appointments/{token}/reschedule: moves it to a time that availability offers.</summary>
internal static class ReschedulePublicAppointment
{
    public sealed record ReschedulePublicAppointmentRequest(DateTimeOffset StartsAt);

    internal sealed class Validator : AbstractValidator<ReschedulePublicAppointmentRequest>
    {
        public Validator() => RuleFor(r => r.StartsAt).NotEqual(default(DateTimeOffset)).WithErrorCode("required");
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/appointments/{token}/reschedule", HandleAsync)
            .WithName("ReschedulePublicAppointment")
            .WithTags("PublicBooking")
            .RequireRateLimiting(PublicRateLimiting.BookingPolicy)
            .Produces<PublicAppointmentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        [FromRoute] string slug, [FromRoute] string token, ReschedulePublicAppointmentRequest request,
        PublicAppointments appointments, IAvailabilityService availability, IAppointmentSelfService selfService,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        _ = slug; // Resolved to the tenant by PublicTenantFilter; declared so it appears in the OpenAPI contract.
        var (appointment, business) = await appointments.LoadAsync(token, cancellationToken);
        appointments.EnsureChangeable(appointment, business);

        var date = DateOnly.FromDateTime(BusinessTimeZone.ToLocal(request.StartsAt, business.TimeZone).DateTime);
        BookingRules.EnsureBookableDate(date, business.TimeZone, clock);

        // Same rule as booking: only a time that availability offers right now (ignoring this appointment itself).
        var offered = await availability.GetAsync(
            appointment.ProfessionalId, appointment.ServiceId, date, appointment.Id, cancellationToken);
        if (!offered.Slots.Any(s => s.StartsAt == request.StartsAt))
        {
            throw BookingRules.SlotUnavailable();
        }

        ManagedAppointment moved;
        try
        {
            moved = await selfService.RescheduleAsync(appointment.Id, request.StartsAt, cancellationToken);
        }
        catch (AppException ex) when (ex.Code == "appointment_overlap")
        {
            throw BookingRules.SlotUnavailable();
        }

        return TypedResults.Ok(await appointments.BuildAsync(moved, business, cancellationToken));
    }
}

/// <summary>POST /api/public/{slug}/appointments/{token}/cancel: the customer cancels their own appointment.</summary>
internal static class CancelPublicAppointment
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/appointments/{token}/cancel", HandleAsync)
            .WithName("CancelPublicAppointment")
            .WithTags("PublicBooking")
            .RequireRateLimiting(PublicRateLimiting.BookingPolicy)
            .Produces<PublicAppointmentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        [FromRoute] string slug, [FromRoute] string token, PublicAppointments appointments,
        IAppointmentSelfService selfService, CancellationToken cancellationToken)
    {
        _ = slug; // Resolved to the tenant by PublicTenantFilter; declared so it appears in the OpenAPI contract.
        var (appointment, business) = await appointments.LoadAsync(token, cancellationToken);
        appointments.EnsureChangeable(appointment, business);

        var cancelled = await selfService.CancelAsync(appointment.Id, cancellationToken);
        return TypedResults.Ok(await appointments.BuildAsync(cancelled, business, cancellationToken));
    }
}
