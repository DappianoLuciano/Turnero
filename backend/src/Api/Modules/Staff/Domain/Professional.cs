using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.Staff.Domain;

/// <summary>
/// A professional who takes appointments (a barber, a stylist...). Not necessarily a user of the panel.
/// Tenant data: RLS by business_id.
/// </summary>
public sealed class Professional : ITenantEntity
{
    public const int NameMaxLength = 120;
    public const int MaxServices = 200;

    private Professional()
    {
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Inactive professionals stay in history but cannot receive new appointments.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Professional Create(Guid businessId, string name, DateTimeOffset now)
    {
        var professional = new Professional
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            IsActive = true,
            CreatedAt = BusinessTimeZone.EnsureUtc(now),
        };
        professional.Apply(name, now);
        return professional;
    }

    public void Update(string name, bool isActive, DateTimeOffset now)
    {
        Apply(name, now);
        IsActive = isActive;
    }

    private void Apply(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        UpdatedAt = BusinessTimeZone.EnsureUtc(now);
    }
}

/// <summary>Link: the professional performs the service. Tenant data (RLS by business_id).</summary>
public sealed class ProfessionalService : ITenantEntity
{
    private ProfessionalService()
    {
    }

    public Guid BusinessId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid ServiceId { get; private set; }

    public static ProfessionalService Create(Guid businessId, Guid professionalId, Guid serviceId) =>
        new() { BusinessId = businessId, ProfessionalId = professionalId, ServiceId = serviceId };
}
