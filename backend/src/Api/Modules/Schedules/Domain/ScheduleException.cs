using Api.Shared.Persistence;

namespace Api.Modules.Schedules.Domain;

/// <summary>
/// A whole local day without service: a holiday of the business (no professional) or a day off of one professional.
/// Tenant data: RLS by business_id.
/// </summary>
public sealed class ScheduleException : ITenantEntity
{
    public const int ReasonMaxLength = 120;

    private ScheduleException()
    {
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }

    /// <summary>Null: the whole business is closed that day.</summary>
    public Guid? ProfessionalId { get; private set; }

    /// <summary>Local calendar day of the business.</summary>
    public DateOnly Date { get; private set; }

    public string? Reason { get; private set; }

    public static ScheduleException Create(Guid businessId, Guid? professionalId, DateOnly date, string? reason) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            ProfessionalId = professionalId,
            Date = date,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };
}
