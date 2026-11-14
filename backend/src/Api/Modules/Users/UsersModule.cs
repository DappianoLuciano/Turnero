using Api.Modules.Users.Contracts;
using Api.Modules.Users.Domain;
using Api.Modules.Users.Features;
using Api.Modules.Users.Features.Auth;
using Api.Shared.Hosting;
using Microsoft.AspNetCore.Identity;

namespace Api.Modules.Users;

/// <summary>Users, business memberships (roles) and authentication.</summary>
internal static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<AuthSessionIssuer>();
        services.AddScoped<IUserProvisioning, UserProvisioning>();
        return services;
    }

    public static void MapUsersEndpoints(ApiRouteGroups groups)
    {
        Login.Map(groups.Anonymous);
        Refresh.Map(groups.Anonymous);
        Logout.Map(groups.Anonymous);
        GetMe.Map(groups.Tenant);
    }
}
