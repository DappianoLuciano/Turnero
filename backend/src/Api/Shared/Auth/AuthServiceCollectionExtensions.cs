using Api.Shared.Hosting;
using Api.Shared.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Shared.Auth;

internal static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        var jwtOptions = services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.Section)
            .ValidateDataAnnotations();
        if (!BuildTooling.IsOpenApiGeneration)
        {
            jwtOptions.ValidateOnStart();
        }

        services.AddSingleton<TokenService>();
        services.AddSingleton<ClaimsTenantResolver>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = TokenService.SigningKey(jwt.Value),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = AppClaims.Subject,
                    RoleClaimType = AppClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Member, p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.BusinessId))
            .AddPolicy(AuthPolicies.Owner, p => p
                .RequireClaim(AppClaims.BusinessId)
                .RequireRole(nameof(BusinessRole.Owner)))
            .AddPolicy(AuthPolicies.OwnerOrReception, p => p
                .RequireClaim(AppClaims.BusinessId)
                .RequireRole(nameof(BusinessRole.Owner), nameof(BusinessRole.Reception)));

        return services;
    }
}
