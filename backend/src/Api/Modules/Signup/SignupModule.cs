using Api.Modules.Signup.Features;
using Api.Shared.Hosting;

namespace Api.Modules.Signup;

/// <summary>
/// Self-service registration: creates a business and its owner. Owns no tables: it composes Businesses, Users and
/// (through business creation) Subscriptions via their contracts.
/// </summary>
internal static class SignupModule
{
    public static IServiceCollection AddSignupModule(this IServiceCollection services) => services;

    public static void MapSignupEndpoints(ApiRouteGroups groups)
    {
        SignupBusiness.Map(groups.Anonymous);
    }
}
