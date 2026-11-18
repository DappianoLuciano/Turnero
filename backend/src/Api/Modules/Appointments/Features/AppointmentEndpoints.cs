using Api.Modules.Appointments.Domain;
using Api.Modules.Businesses.Contracts;
using Api.Modules.Customers.Contracts;
using Api.Modules.Services.Contracts;
using Api.Modules.Staff.Contracts;
using Api.Shared.Errors;
using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Api.Shared.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Modules.Appointments.Features;

public sealed record AppointmentResponse(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    Guid ProfessionalId,
    string ProfessionalName,
    Guid ServiceId,
    string ServiceName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    AppointmentStatus Status,
    decimal Price,
    string Currency,
    string? Notes);

/// <summary>GET /api/appointments?date=YYYY-MM-DD[&amp;professionalId=]: the appointments of one local day.</summary>
internal static class ListAppointments
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/appointments", HandleAsync)
            .WithName("ListAppointments")
            .WithTags("Appointments")
            .Produces<IReadOnlyList<AppointmentResponse>>();

    private static async Task<IResult> HandleAsync(
        DateOnly date, Guid? professionalId, AppDbContext db, IBusinessDirectory businesses,
        AppointmentViews views, CancellationToken cancellationToken)
    {
        var business = await businesses.GetCurrentAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");

        // The day is a local calendar day of the business: convert its bounds to UTC instants.
        var from = BusinessTimeZone.ToUtc(date.ToDateTime(TimeOnly.MinValue), business.TimeZone);
        var to = BusinessTimeZone.ToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), business.TimeZone);

        var query = db.Set<Appointment>().AsNoTracking().Where(a => a.StartsAt >= from && a.StartsAt < to);
        if (professionalId is { } id)
        {
            query = query.Where(a => a.ProfessionalId == id);
        }

        var appointments = await query.OrderBy(a => a.StartsAt).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        return TypedResults.Ok(await views.BuildAsync(appointments, cancellationToken));
    }
}

/// <summary>POST /api/appointments (any member of the business).</summary>
internal static class CreateAppointment
{
    public sealed record CreateAppointmentRequest(
        Guid CustomerId, Guid ProfessionalId, Guid ServiceId, DateTimeOffset StartsAt, string? Notes);

    internal sealed class Validator : AbstractValidator<CreateAppointmentRequest>
    {
        public Validator()
        {
            RuleFor(r => r.CustomerId).NotEmpty().WithErrorCode("required");
            AppointmentRules.Slot(this, r => r.ProfessionalId, r => r.ServiceId, r => r.StartsAt, r => r.Notes);
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/appointments", HandleAsync)
            .WithName("CreateAppointment")
            .WithTags("Appointments")
            .Produces<AppointmentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        CreateAppointmentRequest request, AppDbContext db, ITenantContext tenant, ICustomerDirectory customers,
        AppointmentBooking booking, AppointmentViews views, TimeProvider clock, CancellationToken cancellationToken)
    {
        var customer = (await customers.GetByIdsAsync([request.CustomerId], cancellationToken)).SingleOrDefault()
            ?? throw AppointmentErrors.Invalid("customer_not_found", "Customer not found.");
        if (customer.IsArchived)
        {
            throw AppointmentErrors.Invalid("customer_archived", "The customer is archived.");
        }

        var service = await booking.ResolveAsync(request.ProfessionalId, request.ServiceId, null, null, cancellationToken);
        var appointment = Appointment.Create(tenant.RequireBusinessId(), request.CustomerId, request.ProfessionalId,
            request.ServiceId, request.StartsAt, service.DurationMinutes, new Money(service.Price, service.Currency),
            request.Notes, clock.GetUtcNow());

        db.Set<Appointment>().Add(appointment);
        await AppointmentErrors.SaveAsync(db, cancellationToken);
        var response = (await views.BuildAsync([appointment], cancellationToken)).Single();
        return TypedResults.Created($"/api/appointments/{appointment.Id}", response);
    }
}

/// <summary>PUT /api/appointments/{id}: changes professional, service, time or notes of a scheduled appointment.</summary>
internal static class UpdateAppointment
{
    public sealed record UpdateAppointmentRequest(Guid ProfessionalId, Guid ServiceId, DateTimeOffset StartsAt, string? Notes);

    internal sealed class Validator : AbstractValidator<UpdateAppointmentRequest>
    {
        public Validator() =>
            AppointmentRules.Slot(this, r => r.ProfessionalId, r => r.ServiceId, r => r.StartsAt, r => r.Notes);
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPut("/appointments/{id:guid}", HandleAsync)
            .WithName("UpdateAppointment")
            .WithTags("Appointments")
            .Produces<AppointmentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id, UpdateAppointmentRequest request, AppDbContext db, AppointmentBooking booking, AppointmentViews views,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var appointment = await db.Set<Appointment>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw AppointmentErrors.NotFound();
        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            throw AppException.Conflict("appointment_not_editable", "Only scheduled appointments can be edited.");
        }

        var service = await booking.ResolveAsync(
            request.ProfessionalId, request.ServiceId, appointment.ProfessionalId, appointment.ServiceId, cancellationToken);
        var serviceChanged = request.ServiceId != appointment.ServiceId;
        // Same service: keep the duration and price agreed when it was booked.
        var duration = serviceChanged ? service.DurationMinutes : (int)(appointment.EndsAt - appointment.StartsAt).TotalMinutes;
        var price = serviceChanged ? new Money(service.Price, service.Currency) : new Money(appointment.Price, appointment.Currency);

        appointment.Reschedule(request.ProfessionalId, request.ServiceId, request.StartsAt, duration, price,
            request.Notes, clock.GetUtcNow());
        await AppointmentErrors.SaveAsync(db, cancellationToken);
        return TypedResults.Ok((await views.BuildAsync([appointment], cancellationToken)).Single());
    }
}

/// <summary>PUT /api/appointments/{id}/status: complete, cancel, mark no-show, or reopen.</summary>
internal static class ChangeAppointmentStatus
{
    public sealed record ChangeAppointmentStatusRequest(AppointmentStatus Status);

    internal sealed class Validator : AbstractValidator<ChangeAppointmentStatusRequest>
    {
        public Validator() => RuleFor(r => r.Status).IsInEnum().WithErrorCode("invalid");
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPut("/appointments/{id:guid}/status", HandleAsync)
            .WithName("ChangeAppointmentStatus")
            .WithTags("Appointments")
            .Produces<AppointmentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id, ChangeAppointmentStatusRequest request, AppDbContext db, AppointmentViews views, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var appointment = await db.Set<Appointment>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw AppointmentErrors.NotFound();

        // Reopening a cancelled appointment may collide with another one that took its slot: the database decides.
        appointment.SetStatus(request.Status, clock.GetUtcNow());
        await AppointmentErrors.SaveAsync(db, cancellationToken);
        return TypedResults.Ok((await views.BuildAsync([appointment], cancellationToken)).Single());
    }
}

/// <summary>Validates professional and service of a booking against the other modules' public contracts.</summary>
internal sealed class AppointmentBooking(IProfessionalDirectory professionals, IServiceCatalog catalog)
{
    /// <summary>
    /// Returns the service. <paramref name="currentProfessionalId"/> / <paramref name="currentServiceId"/> are the
    /// values already stored when rescheduling: keeping them is allowed even if they were deactivated since.
    /// </summary>
    public async Task<ServiceSummary> ResolveAsync(
        Guid professionalId, Guid serviceId, Guid? currentProfessionalId, Guid? currentServiceId,
        CancellationToken cancellationToken)
    {
        var professional = (await professionals.GetByIdsAsync([professionalId], cancellationToken)).SingleOrDefault()
            ?? throw AppointmentErrors.Invalid("professional_not_found", "Professional not found.");
        var service = (await catalog.GetByIdsAsync([serviceId], cancellationToken)).SingleOrDefault()
            ?? throw AppointmentErrors.Invalid("service_not_found", "Service not found.");

        if (!professional.IsActive && professionalId != currentProfessionalId)
        {
            throw AppointmentErrors.Invalid("professional_inactive", "The professional does not take appointments.");
        }

        if (!service.IsActive && serviceId != currentServiceId)
        {
            throw AppointmentErrors.Invalid("service_inactive", "The service is not available.");
        }

        if (!professional.ServiceIds.Contains(serviceId))
        {
            throw AppointmentErrors.Invalid("professional_service_mismatch",
                "The professional does not perform this service.");
        }

        return service;
    }
}

/// <summary>Builds responses, resolving names through the other modules' public contracts.</summary>
internal sealed class AppointmentViews(
    ICustomerDirectory customers, IProfessionalDirectory professionals, IServiceCatalog catalog)
{
    public async Task<IReadOnlyList<AppointmentResponse>> BuildAsync(
        IReadOnlyList<Appointment> appointments, CancellationToken cancellationToken)
    {
        var customerNames = (await customers.GetByIdsAsync(
                appointments.Select(a => a.CustomerId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.Id, c => c.Name);
        var professionalNames = (await professionals.GetByIdsAsync(
                appointments.Select(a => a.ProfessionalId).Distinct().ToList(), cancellationToken))
            .ToDictionary(p => p.Id, p => p.Name);
        var serviceNames = (await catalog.GetByIdsAsync(
                appointments.Select(a => a.ServiceId).Distinct().ToList(), cancellationToken))
            .ToDictionary(s => s.Id, s => s.Name);

        return appointments.Select(a => new AppointmentResponse(
            a.Id,
            a.CustomerId, customerNames.GetValueOrDefault(a.CustomerId, string.Empty),
            a.ProfessionalId, professionalNames.GetValueOrDefault(a.ProfessionalId, string.Empty),
            a.ServiceId, serviceNames.GetValueOrDefault(a.ServiceId, string.Empty),
            a.StartsAt, a.EndsAt, a.Status, a.Price, a.Currency, a.Notes)).ToList();
    }
}

internal static class AppointmentErrors
{
    public static AppException NotFound() => AppException.NotFound("appointment_not_found", "Appointment not found.");

    public static AppException Invalid(string code, string title) =>
        new(StatusCodes.Status400BadRequest, code, title);

    /// <summary>Saves; the exclusion constraint of the database is the source of truth against overlaps.</summary>
    public static async Task SaveAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            throw AppException.Conflict("appointment_overlap",
                "The professional already has an appointment at that time.");
        }
    }
}

internal static class AppointmentRules
{
    public static void Slot<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, Guid>> professionalId,
        System.Linq.Expressions.Expression<Func<T, Guid>> serviceId,
        System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> startsAt,
        System.Linq.Expressions.Expression<Func<T, string?>> notes)
    {
        validator.RuleFor(professionalId).NotEmpty().WithErrorCode("required");
        validator.RuleFor(serviceId).NotEmpty().WithErrorCode("required");
        validator.RuleFor(startsAt).NotEqual(default(DateTimeOffset)).WithErrorCode("required");
        validator.RuleFor(notes).MaximumLength(Appointment.NotesMaxLength).WithErrorCode("too_long");
    }
}
