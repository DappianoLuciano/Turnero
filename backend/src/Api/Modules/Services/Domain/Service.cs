using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.Services.Domain;

/// <summary>A bookable service of the catalog (e.g. a haircut). Tenant data: RLS by business_id.</summary>
public sealed class Service : ITenantEntity
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 500;
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 480;

    private Service()
    {
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int DurationMinutes { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = Money.DefaultCurrency;

    /// <summary>Inactive services stay in history but cannot be booked.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Service Create(Guid businessId, string name, string? description, int durationMinutes,
        Money price, DateTimeOffset now)
    {
        var service = new Service
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            IsActive = true,
            CreatedAt = BusinessTimeZone.EnsureUtc(now),
        };
        service.Apply(name, description, durationMinutes, price, now);
        return service;
    }

    public void Update(string name, string? description, int durationMinutes, Money price, bool isActive,
        DateTimeOffset now)
    {
        Apply(name, description, durationMinutes, price, now);
        IsActive = isActive;
    }

    private void Apply(string name, string? description, int durationMinutes, Money price, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(durationMinutes, MinDurationMinutes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(durationMinutes, MaxDurationMinutes);
        ArgumentOutOfRangeException.ThrowIfNegative(price.Amount);

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DurationMinutes = durationMinutes;
        Price = price.Amount;
        Currency = price.Currency;
        UpdatedAt = BusinessTimeZone.EnsureUtc(now);
    }
}
