namespace Api.Modules.Subscriptions.Contracts;

public enum SubscriptionState
{
    /// <summary>Free trial still running.</summary>
    Trial,

    /// <summary>Paid period running.</summary>
    Active,

    /// <summary>Trial over and not paid (or paid period over): the business is locked.</summary>
    Expired,
}

/// <summary>Public view of a business's subscription.</summary>
public sealed record SubscriptionSummary(
    SubscriptionState State, DateTimeOffset TrialEndsAt, DateTimeOffset? PaidUntil, int DaysLeft);

/// <summary>Starts the subscription of a brand-new business. Every business gets a trial when it is created.</summary>
public interface ISubscriptionProvisioning
{
    /// <summary>
    /// Adds the trial subscription to the current unit of work; the caller saves it together with the business,
    /// inside that business's tenant scope.
    /// </summary>
    void StartTrial(Guid businessId);
}

/// <summary>Operator actions (no public endpoint): see <c>dotnet run --project src/Api -- activate</c>.</summary>
public interface ISubscriptionAdmin
{
    /// <summary>
    /// Registers a payment: extends the paid period by <paramref name="days"/> (from now, or from the end of the
    /// current paid period if it has not ended). Must be called with no open transaction.
    /// </summary>
    Task<SubscriptionSummary> ActivateAsync(Guid businessId, int days, CancellationToken cancellationToken = default);
}
