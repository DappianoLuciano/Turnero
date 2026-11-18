using Api.Shared.Persistence;

namespace Api.Modules.Schedules.Domain;

/// <summary>
/// One block of the weekly working hours of a professional, in the business's local time:
/// minutes since local midnight on a day of the week. A day may have several blocks (split shifts).
/// Tenant data: RLS by business_id. The database forbids overlapping blocks of the same professional and day.
/// </summary>
public sealed class WorkingBlock : ITenantEntity
{
    public const int MinutesPerDay = 24 * 60;

    private WorkingBlock()
    {
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid ProfessionalId { get; private set; }

    /// <summary>0 = Sunday ... 6 = Saturday (<see cref="System.DayOfWeek"/>).</summary>
    public int DayOfWeek { get; private set; }

    public int StartMinute { get; private set; }
    public int EndMinute { get; private set; }

    public static WorkingBlock Create(Guid businessId, Guid professionalId, int dayOfWeek, int startMinute, int endMinute)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayOfWeek);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dayOfWeek, 6);
        ArgumentOutOfRangeException.ThrowIfNegative(startMinute);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(endMinute, MinutesPerDay);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startMinute, endMinute);
        return new WorkingBlock
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            ProfessionalId = professionalId,
            DayOfWeek = dayOfWeek,
            StartMinute = startMinute,
            EndMinute = endMinute,
        };
    }
}
