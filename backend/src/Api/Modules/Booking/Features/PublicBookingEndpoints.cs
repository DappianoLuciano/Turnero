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

// Everything in this file is PUBLIC (no login): responses carry only what a customer needs to book.
// Never add customers, prices of past appointments, notes or anything about other people.

public sealed record PublicBusinessResponse(string Name, string Slug, string TimeZone, string Currency);

public sealed record PublicServiceResponse(Guid Id, string Name, int DurationMinutes, decimal Price, string Currency);

public sealed record PublicProfessionalResponse(Guid Id, string Name, IReadOnlyList<Guid> ServiceIds);

public sealed record PublicCatalogResponse(
    PublicBusinessResponse Business,
    IReadOnlyList<PublicServiceResponse> Services,
    IReadOnlyList<PublicProfessionalResponse> Professionals);

public sealed record PublicSlotResponse(DateTimeOffset StartsAt, int Minutes);

public sealed record PublicAvailabilityResponse(DateOnly Date, bool IsClosed, IReadOnlyList<PublicSlotResponse> Slots);

public sealed record PublicBookingResponse(
    Guid AppointmentId,
    string BusinessName,
    string ServiceName,
    string ProfessionalName,
    string CustomerName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal Price,
    string Currency);

/// <summary>GET /api/public/{slug}/catalog: the business, what it offers and who performs it.</summary>
internal static class GetPublicCatalog
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/catalog", HandleAsync)
            .WithName("GetPublicCatalog")
            .WithTags("PublicBooking")
            .Produces<PublicCatalogResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        [FromRoute] string slug, IBusinessDirectory businesses, IServiceCatalog catalog,
        IProfessionalDirectory professionals, CancellationToken cancellationToken)
    {
        _ = slug; // Resolved to the tenant by PublicTenantFilter; declared so it appears in the OpenAPI contract.
        var business = await businesses.GetCurrentAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");
        var allServices = await catalog.ListActiveAsync(cancellationToken);
        var allProfessionals = await professionals.ListActiveAsync(cancellationToken);

        // Only what can actually be booked: services someone performs and professionals who perform something.
        var activeServiceIds = allServices.Select(s => s.Id).ToHashSet();
        var bookable = allProfessionals
            .Select(p => new PublicProfessionalResponse(p.Id, p.Name, p.ServiceIds.Where(activeServiceIds.Contains).ToList()))
            .Where(p => p.ServiceIds.Count > 0)
            .ToList();
        var offered = bookable.SelectMany(p => p.ServiceIds).ToHashSet();

        return TypedResults.Ok(new PublicCatalogResponse(
            new PublicBusinessResponse(business.Name, business.Slug, business.TimeZone, business.Currency),
            allServices.Where(s => offered.Contains(s.Id))
                .Select(s => new PublicServiceResponse(s.Id, s.Name, s.DurationMinutes, s.Price, s.Currency)).ToList(),
            bookable));
    }
}

/// <summary>GET /api/public/{slug}/availability?professionalId=&amp;serviceId=&amp;date=: free start times of a day.</summary>
internal static class GetPublicAvailability
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/availability", HandleAsync)
            .WithName("GetPublicAvailability")
            .WithTags("PublicBooking")
            .Produces<PublicAvailabilityResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        [FromRoute] string slug, Guid professionalId, Guid serviceId, DateOnly date, IBusinessDirectory businesses,
        IAvailabilityService availability, TimeProvider clock, CancellationToken cancellationToken)
    {
        _ = slug; // Resolved to the tenant by PublicTenantFilter; declared so it appears in the OpenAPI contract.
        var business = await businesses.GetCurrentAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");
        BookingRules.EnsureBookableDate(date, business.TimeZone, clock);

        var result = await availability.GetAsync(professionalId, serviceId, date, cancellationToken);
        return TypedResults.Ok(new PublicAvailabilityResponse(
            result.Date, result.IsClosed, result.Slots.Select(s => new PublicSlotResponse(s.StartsAt, s.Minutes)).ToList()));
    }
}

/// <summary>
/// POST /api/public/{slug}/appointments: books a time without an account. The customer is found by phone or created
/// (an existing customer keeps the name the business has). Only a time that availability offers can be booked.
/// </summary>
internal static class CreatePublicBooking
{
    public const int PhoneMaxLength = 30;

    public sealed record CreatePublicBookingRequest(
        Guid ProfessionalId, Guid ServiceId, DateTimeOffset StartsAt, string CustomerName, string CustomerPhone);

    internal sealed class Validator : AbstractValidator<CreatePublicBookingRequest>
    {
        public Validator()
        {
            RuleFor(r => r.ProfessionalId).NotEmpty().WithErrorCode("required");
            RuleFor(r => r.ServiceId).NotEmpty().WithErrorCode("required");
            RuleFor(r => r.StartsAt).NotEqual(default(DateTimeOffset)).WithErrorCode("required");
            RuleFor(r => r.CustomerName).NotEmpty().WithErrorCode("required").MaximumLength(120).WithErrorCode("too_long");
            RuleFor(r => r.CustomerPhone).NotEmpty().WithErrorCode("required")
                .MaximumLength(PhoneMaxLength).WithErrorCode("too_long");
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/appointments", HandleAsync)
            .WithName("CreatePublicBooking")
            .WithTags("PublicBooking")
            .RequireRateLimiting(PublicRateLimiting.BookingPolicy)
            .Produces<PublicBookingResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        [FromRoute] string slug, CreatePublicBookingRequest request, IBusinessDirectory businesses,
        IServiceCatalog catalog, IProfessionalDirectory professionals, IAvailabilityService availability,
        ICustomerRegistry customers, IAppointmentCreator appointments, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        _ = slug; // Resolved to the tenant by PublicTenantFilter; declared so it appears in the OpenAPI contract.
        var business = await businesses.GetCurrentAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");

        var date = DateOnly.FromDateTime(BusinessTimeZone.ToLocal(request.StartsAt, business.TimeZone).DateTime);
        BookingRules.EnsureBookableDate(date, business.TimeZone, clock);

        // The time must be one the availability offers right now: it is the single source of truth for working
        // hours, closed days and existing appointments.
        var offered = await availability.GetAsync(request.ProfessionalId, request.ServiceId, date, cancellationToken);
        if (!offered.Slots.Any(s => s.StartsAt == request.StartsAt))
        {
            throw BookingRules.SlotUnavailable();
        }

        var customer = await customers.FindOrCreateByPhoneAsync(request.CustomerName, request.CustomerPhone, cancellationToken);

        CreatedAppointment created;
        try
        {
            created = await appointments.CreateAsync(
                customer.Id, request.ProfessionalId, request.ServiceId, request.StartsAt, cancellationToken);
        }
        catch (AppException ex) when (ex.Code == "appointment_overlap")
        {
            // Someone else took the time between the check above and the insert: the database decided.
            throw BookingRules.SlotUnavailable();
        }

        var service = (await catalog.GetByIdsAsync([request.ServiceId], cancellationToken)).Single();
        var professional = (await professionals.GetByIdsAsync([request.ProfessionalId], cancellationToken)).Single();
        return TypedResults.Created($"/api/public/{business.Slug}/appointments/{created.Id}", new PublicBookingResponse(
            created.Id, business.Name, service.Name, professional.Name, customer.Name, created.StartsAt, created.EndsAt,
            created.Price, created.Currency));
    }
}

internal static class BookingRules
{
    /// <summary>How far ahead a customer can book.</summary>
    public const int MaxDaysAhead = 90;

    public static void EnsureBookableDate(DateOnly date, string timeZone, TimeProvider clock)
    {
        var today = DateOnly.FromDateTime(BusinessTimeZone.ToLocal(clock.GetUtcNow(), timeZone).DateTime);
        if (date < today || date.DayNumber - today.DayNumber > MaxDaysAhead)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "date_out_of_range",
                "That date cannot be booked.");
        }
    }

    public static AppException SlotUnavailable() =>
        AppException.Conflict("slot_unavailable", "That time is no longer available.");
}
