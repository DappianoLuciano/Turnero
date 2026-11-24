using Api.Modules.Subscriptions.Contracts;
using Api.Modules.Subscriptions.Domain;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Subscriptions.Features;

public sealed record SubscriptionResponse(
    SubscriptionState State, DateTimeOffset TrialEndsAt, DateTimeOffset? PaidUntil, int DaysLeft);

/// <summary>GET /api/subscription: trial/paid status of the current business. Reachable even when expired.</summary>
internal static class GetSubscription
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/subscription", HandleAsync)
            .WithName("GetSubscription")
            .WithTags("Subscriptions")
            .WithMetadata(new AllowWithoutSubscriptionAttribute())
            .Produces<SubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var subscription = await db.Set<Subscription>().AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw AppException.NotFound("subscription_not_found", "The business has no subscription.");
        var s = subscription.SummaryAt(clock.GetUtcNow());
        return TypedResults.Ok(new SubscriptionResponse(s.State, s.TrialEndsAt, s.PaidUntil, s.DaysLeft));
    }
}
