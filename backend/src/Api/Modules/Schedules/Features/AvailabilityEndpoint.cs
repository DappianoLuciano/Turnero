using Api.Modules.Appointments.Contracts;
using Api.Modules.Businesses.Contracts;
using Api.Modules.Schedules.Contracts;
using Api.Modules.Schedules.Domain;
using Api.Modules.Services.Contracts;
using Api.Modules.Staff.Contracts;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Schedules.Features;

/// <summary>A free time at which the service can start.</summary>
public sealed record AvailableSlot(DateTimeOffset StartsAt, int Minutes);

/// <param name="ClosedReason">Set when the day is closed (holiday or day off): there are no slots.</param>
public sealed record AvailabilityResponse(
    DateOnly Date, int DurationMinutes, string? ClosedReason, bool IsClosed, IReadOnlyList<AvailableSlot> Slots);

/// <summary>
/// GET /api/availability?professionalId=&amp;serviceId=&amp;date=: free start times of a professional for a service on
/// a local day (see <see cref="IAvailabilityService"/>).
/// </summary>
internal static class GetAvailability
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/availability", HandleAsync)
            .WithName("GetAvailability")
            .WithTags("Schedules")
            .Produces<AvailabilityResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid professionalId, Guid serviceId, DateOnly date, IAvailabilityService availability,
        CancellationToken cancellationToken)
    {
        var result = await availability.GetAsync(professionalId, serviceId, date, cancellationToken: cancellationToken);
        return TypedResults.Ok(new AvailabilityResponse(
            result.Date, result.DurationMinutes, result.ClosedReason, result.IsClosed,
            result.Slots.Select(s => new AvailableSlot(s.StartsAt, s.Minutes)).ToList()));
    }
}

internal sealed class AvailabilityService(
    AppDbContext db, IBusinessDirectory businesses, IProfessionalDirectory professionals, IServiceCatalog catalog,
    IAppointmentOccupancy occupancy, TimeProvider clock) : IAvailabilityService
{
    /// <summary>Start times are offered every 15 minutes inside each working block.</summary>
    public const int StepMinutes = 15;

    public async Task<AvailabilityResult> GetAsync(
        Guid professionalId, Guid serviceId, DateOnly date, Guid? ignoreAppointmentId = null,
        CancellationToken cancellationToken = default)
    {
        var business = await businesses.GetCurrentAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");
        var professional = (await professionals.GetByIdsAsync([professionalId], cancellationToken)).SingleOrDefault()
            ?? throw AppException.NotFound("professional_not_found", "Professional not found.");
        var service = (await catalog.GetByIdsAsync([serviceId], cancellationToken)).SingleOrDefault()
            ?? throw AppException.NotFound("service_not_found", "Service not found.");
        if (!professional.IsActive || !service.IsActive || !professional.ServiceIds.Contains(serviceId))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "professional_service_mismatch",
                "The professional does not take appointments for this service.");
        }

        var closed = await db.Set<ScheduleException>().AsNoTracking()
            .Where(e => e.Date == date && (e.ProfessionalId == null || e.ProfessionalId == professionalId))
            .OrderBy(e => e.ProfessionalId)
            .FirstOrDefaultAsync(cancellationToken);
        if (closed is not null)
        {
            return new AvailabilityResult(date, service.DurationMinutes, true, closed.Reason, []);
        }

        var dayOfWeek = (int)date.DayOfWeek;
        var blocks = await db.Set<WorkingBlock>().AsNoTracking()
            .Where(b => b.ProfessionalId == professionalId && b.DayOfWeek == dayOfWeek)
            .OrderBy(b => b.StartMinute)
            .ToListAsync(cancellationToken);

        var (dayStart, dayEnd) = BusinessTimeZone.LocalDaysToUtc(date, date, business.TimeZone);
        var taken = await occupancy.GetOccupiedAsync(professionalId, dayStart, dayEnd, ignoreAppointmentId, cancellationToken);
        var now = clock.GetUtcNow();
        var midnight = date.ToDateTime(TimeOnly.MinValue);

        var slots = new List<AvailabilitySlot>();
        foreach (var block in blocks)
        {
            for (var minute = block.StartMinute; minute + service.DurationMinutes <= block.EndMinute; minute += StepMinutes)
            {
                var start = BusinessTimeZone.ToUtc(midnight.AddMinutes(minute), business.TimeZone);
                var end = start.AddMinutes(service.DurationMinutes);
                if (start > now && !taken.Any(t => t.Start < end && t.End > start))
                {
                    slots.Add(new AvailabilitySlot(start, minute));
                }
            }
        }

        return new AvailabilityResult(date, service.DurationMinutes, false, null, slots);
    }
}
