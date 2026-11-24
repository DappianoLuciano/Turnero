using Api.Modules.Subscriptions.Contracts;
using Api.Modules.Subscriptions.Features;
using Api.Shared.Hosting;
using Api.Shared.Tenancy;

namespace Api.Modules.Subscriptions;

/// <summary>
/// Free trial (default 2 days, <c>Subscriptions:TrialDays</c>) and paid periods. When the trial ends without payment
/// the business is locked: tenant and public endpoints answer 402 until the payment is registered (ADR 0014).
/// </summary>
internal static class SubscriptionsModule
{
    public static IServiceCollection AddSubscriptionsModule(this IServiceCollection services)
    {
        services.AddOptions<SubscriptionOptions>().BindConfiguration("Subscriptions");
        services.AddScoped<ISubscriptionProvisioning, SubscriptionProvisioning>();
        services.AddScoped<ISubscriptionAdmin, SubscriptionAdmin>();
        services.AddScoped<ITenantAccessGate, SubscriptionGate>();
        return services;
    }

    public static void MapSubscriptionsEndpoints(ApiRouteGroups groups)
    {
        GetSubscription.Map(groups.Tenant);
    }
}
