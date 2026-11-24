namespace Api.Modules.Schedules.Contracts;

/// <summary>A free time at which a service can start.</summary>
public sealed record AvailabilitySlot(DateTimeOffset StartsAt, int Minutes);

/// <param name="ClosedReason">Set when the day is closed (holiday or day off): there are no slots.</param>
public sealed record AvailabilityResult(
    DateOnly Date, int DurationMinutes, bool IsClosed, string? ClosedReason, IReadOnlyList<AvailabilitySlot> Slots);

/// <summary>Free start times of a professional for a service on a local day.</summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Working blocks of the day, minus closed days, minus existing appointments, minus times already past.
    /// <paramref name="ignoreAppointmentId"/> leaves one appointment out, so it can be moved to an overlapping time.
    /// Throws 404 for an unknown professional or service and 400 when the professional does not take the service.
    /// </summary>
    Task<AvailabilityResult> GetAsync(
        Guid professionalId, Guid serviceId, DateOnly date, Guid? ignoreAppointmentId = null,
        CancellationToken cancellationToken = default);
}
