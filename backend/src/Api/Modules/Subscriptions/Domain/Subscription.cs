using Api.Modules.Subscriptions.Contracts;
using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.Subscriptions.Domain;

/// <summary>
/// Access to the product for one business: a free trial, then paid periods. One row per business.
/// Tenant data: RLS by business_id.
/// </summary>
public sealed class Subscription : ITenantEntity
{
    public const int MaxActivationDays = 3660;

    private Subscription()
    {
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public DateTimeOffset TrialEndsAt { get; private set; }

    /// <summary>End of the last paid period; null if the business never paid.</summary>
    public DateTimeOffset? PaidUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Subscription StartTrial(Guid businessId, DateTimeOffset now, int trialDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trialDays);
        var utc = BusinessTimeZone.EnsureUtc(now);
        return new Subscription
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            TrialEndsAt = utc.AddDays(trialDays),
            CreatedAt = utc,
            UpdatedAt = utc,
        };
    }

    public SubscriptionState StateAt(DateTimeOffset now)
    {
        if (PaidUntil > now)
        {
            return SubscriptionState.Active;
        }

        return TrialEndsAt > now ? SubscriptionState.Trial : SubscriptionState.Expired;
    }

    public SubscriptionSummary SummaryAt(DateTimeOffset now)
    {
        var state = StateAt(now);
        var end = state switch
        {
            SubscriptionState.Active => PaidUntil!.Value,
            SubscriptionState.Trial => TrialEndsAt,
            _ => now,
        };
        var daysLeft = state == SubscriptionState.Expired ? 0 : (int)Math.Ceiling((end - now).TotalDays);
        return new SubscriptionSummary(state, TrialEndsAt, PaidUntil, daysLeft);
    }

    /// <summary>Registers a payment: extends the paid period by <paramref name="days"/>.</summary>
    public void Activate(DateTimeOffset now, int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, MaxActivationDays);
        var utc = BusinessTimeZone.EnsureUtc(now);
        var from = PaidUntil > utc ? PaidUntil.Value : utc;
        PaidUntil = from.AddDays(days);
        UpdatedAt = utc;
    }
}
