using Api.Modules.Appointments.Contracts;
using Api.Modules.Appointments.Domain;
using Api.Modules.Businesses.Contracts;
using Api.Modules.Customers.Contracts;
using Api.Modules.Services.Contracts;
using Api.Modules.Staff.Contracts;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Appointments.Features;

/// <summary>
/// One scheduled appointment to remind. <c>ManageToken</c> builds the link where the customer can change or cancel.
/// </summary>
public sealed record ReminderResponse(
    Guid AppointmentId,
    string CustomerName,
    string? CustomerPhone,
    string ProfessionalName,
    string ServiceName,
    DateTimeOffset StartsAt,
    DateTimeOffset? ReminderSentAt,
    string ManageToken);

/// <summary>
/// GET /api/appointments/reminders?date=YYYY-MM-DD: scheduled appointments of a local day, with what the business needs
/// to send a WhatsApp reminder by hand (there is no messaging provider).
/// </summary>
internal static class ListReminders
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/appointments/reminders", HandleAsync)
            .WithName("ListReminders")
            .WithTags("Appointments")
            .Produces<IReadOnlyList<ReminderResponse>>();

    private static async Task<IResult> HandleAsync(
        DateOnly date, AppDbContext db, IBusinessDirectory businesses, ICustomerDirectory customers,
        IProfessionalDirectory professionals, IServiceCatalog catalog, IAppointmentLinks links,
        CancellationToken cancellationToken)
    {
        var business = await businesses.GetCurrentAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");
        var (from, to) = BusinessTimeZone.LocalDaysToUtc(date, date, business.TimeZone);

        var appointments = await db.Set<Appointment>().AsNoTracking()
            .Where(a => a.Status == AppointmentStatus.Scheduled && a.StartsAt >= from && a.StartsAt < to)
            .OrderBy(a => a.StartsAt).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        var customerById = (await customers.GetByIdsAsync(appointments.Select(a => a.CustomerId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.Id);
        var professionalNames = (await professionals.GetByIdsAsync(appointments.Select(a => a.ProfessionalId).Distinct().ToList(), cancellationToken))
            .ToDictionary(p => p.Id, p => p.Name);
        var serviceNames = (await catalog.GetByIdsAsync(appointments.Select(a => a.ServiceId).Distinct().ToList(), cancellationToken))
            .ToDictionary(s => s.Id, s => s.Name);

        return TypedResults.Ok(appointments.Select(a =>
        {
            var customer = customerById.GetValueOrDefault(a.CustomerId);
            return new ReminderResponse(
                a.Id, customer?.Name ?? string.Empty, customer?.Phone,
                professionalNames.GetValueOrDefault(a.ProfessionalId, string.Empty),
                serviceNames.GetValueOrDefault(a.ServiceId, string.Empty),
                a.StartsAt, a.ReminderSentAt, links.CreateToken(a.Id));
        }).ToList());
    }
}

/// <summary>POST /api/appointments/{id}/reminder-sent: the business sent (or is sending) the reminder.</summary>
internal static class MarkReminderSent
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/appointments/{id:guid}/reminder-sent", HandleAsync)
            .WithName("MarkReminderSent")
            .WithTags("Appointments")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(Guid id, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var appointment = await db.Set<Appointment>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw AppointmentErrors.NotFound();
        appointment.MarkReminderSent(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
