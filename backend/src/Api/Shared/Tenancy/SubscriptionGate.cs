using Api.Shared.Errors;

namespace Api.Shared.Tenancy;

/// <summary>
/// Decides whether the current tenant may use the product (active trial or paid subscription).
/// Implemented by the Subscriptions module; Shared only knows this seam (ADR 0014).
/// </summary>
public interface ITenantAccessGate
{
    /// <summary>True when the business of the current tenant scope can use the product. Fails closed.</summary>
    Task<bool> IsAllowedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Endpoint metadata: the endpoint stays reachable when the subscription expired (e.g. to read the
/// subscription status itself and show the "pay to continue" screen).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWithoutSubscriptionAttribute : Attribute;

/// <summary>
/// Blocks tenant and public endpoints with 402 <c>subscription_required</c> once the trial ended and the business
/// has not paid. Runs after the transaction filter, so the gate reads with the tenant applied (RLS).
/// </summary>
internal sealed class SubscriptionGateFilter(ITenantAccessGate gate) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var exempt = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<AllowWithoutSubscriptionAttribute>() is not null;
        if (!exempt && !await gate.IsAllowedAsync(context.HttpContext.RequestAborted))
        {
            throw new AppException(StatusCodes.Status402PaymentRequired, "subscription_required",
                "The subscription is not active.");
        }

        return await next(context);
    }
}
