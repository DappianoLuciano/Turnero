using Api.Shared.Hosting;
using Microsoft.OpenApi;

namespace Api.Hosting;

internal static class OpenApiSetup
{
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services, ProductInfo product)
    {
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo { Title = $"{product.Name} API", Version = "v1" };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Access token returned by POST /api/auth/login.",
                };
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
