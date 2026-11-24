using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.Appointments.Domain;

public enum AppointmentStatus
{
    Scheduled,
    Completed,
    Cancelled,
    NoShow,
}

/// <summary>
/// A booked time slot of a professional for a customer and a service. Tenant data: RLS by business_id.
/// The database forbids overlapping appointments of the same professional that still occupy their slot.
/// </summary>
public sealed class Appointment : ITenantEntity
{
    public const int NotesMaxLength = 500;

    private Appointment()
    {
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid ServiceId { get; private set; }

    /// <summary>UTC instant, minute precision.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>UTC instant: <see cref="StartsAt"/> plus the duration of the service when booked.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    public AppointmentStatus Status { get; private set; }

    /// <summary>Price of the service when booked (later catalog changes do not alter it).</summary>
    public decimal Price { get; private set; }

    public string Currency { get; private set; } = Money.DefaultCurrency;
    public string? Notes { get; private set; }

    /// <summary>When the business marked the reminder as sent; null if it was not. Cleared when the time changes.</summary>
    public DateTimeOffset? ReminderSentAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Appointment Create(Guid businessId, Guid customerId, Guid professionalId, Guid serviceId,
        DateTimeOffset startsAt, int durationMinutes, Money price, string? notes, DateTimeOffset now)
    {
        var appointment = new Appointment
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            CustomerId = customerId,
            Status = AppointmentStatus.Scheduled,
            CreatedAt = BusinessTimeZone.EnsureUtc(now),
        };
        appointment.Apply(professionalId, serviceId, startsAt, durationMinutes, price, notes, now);
        return appointment;
    }

    /// <summary>Changes who/what/when. The customer never changes: cancel and book another appointment instead.</summary>
    public void Reschedule(Guid professionalId, Guid serviceId, DateTimeOffset startsAt, int durationMinutes,
        Money price, string? notes, DateTimeOffset now) =>
        Apply(professionalId, serviceId, startsAt, durationMinutes, price, notes, now);

    public void SetStatus(AppointmentStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = BusinessTimeZone.EnsureUtc(now);
    }

    public void MarkReminderSent(DateTimeOffset now) => ReminderSentAt = BusinessTimeZone.EnsureUtc(now);

    private void Apply(Guid professionalId, Guid serviceId, DateTimeOffset startsAt, int durationMinutes,
        Money price, string? notes, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationMinutes);
        ArgumentOutOfRangeException.ThrowIfNegative(price.Amount);

        var start = BusinessTimeZone.EnsureUtc(startsAt);
        start = new DateTimeOffset(start.Ticks - (start.Ticks % TimeSpan.TicksPerMinute), TimeSpan.Zero);

        if (start != StartsAt)
        {
            // A new time needs a new reminder.
            ReminderSentAt = null;
        }

        ProfessionalId = professionalId;
        ServiceId = serviceId;
        StartsAt = start;
        EndsAt = start.AddMinutes(durationMinutes);
        Price = price.Amount;
        Currency = price.Currency;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = BusinessTimeZone.EnsureUtc(now);
    }
}
