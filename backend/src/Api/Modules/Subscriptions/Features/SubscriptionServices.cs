using Api.Modules.Subscriptions.Contracts;
using Api.Modules.Subscriptions.Domain;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Subscriptions.Features;

public sealed class SubscriptionOptions
{
    public const int DefaultTrialDays = 2;

    public int TrialDays { get; set; } = DefaultTrialDays;
}

internal sealed class SubscriptionProvisioning(
    AppDbContext db, TimeProvider clock, Microsoft.Extensions.Options.IOptions<SubscriptionOptions> options)
    : ISubscriptionProvisioning
{
    public void StartTrial(Guid businessId) =>
        db.Set<Subscription>().Add(Subscription.StartTrial(businessId, clock.GetUtcNow(), options.Value.TrialDays));
}

internal sealed class SubscriptionAdmin(AppDbContext db, TenantScope scope, TimeProvider clock) : ISubscriptionAdmin
{
    public Task<SubscriptionSummary> ActivateAsync(Guid businessId, int days, CancellationToken cancellationToken = default) =>
        scope.RunAsync(businessId, null, async ct =>
        {
            var subscription = await db.Set<Subscription>().SingleOrDefaultAsync(ct)
                ?? throw AppException.NotFound("subscription_not_found", "The business has no subscription.");
            var now = clock.GetUtcNow();
            subscription.Activate(now, days);
            await db.SaveChangesAsync(ct);
            return subscription.SummaryAt(now);
        }, cancellationToken);
}

/// <summary>Gate used by the tenant and public endpoint groups. A business without subscription row is locked.</summary>
internal sealed class SubscriptionGate(AppDbContext db, TimeProvider clock) : ITenantAccessGate
{
    public async Task<bool> IsAllowedAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var subscription = await db.Set<Subscription>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return subscription is not null && subscription.StateAt(now) != SubscriptionState.Expired;
    }
}
