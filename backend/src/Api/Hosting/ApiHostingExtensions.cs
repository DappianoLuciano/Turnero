using Api.Shared.Errors;
using Api.Shared.Hosting;
using Api.Shared.Validation;
using Scalar.AspNetCore;

namespace Api.Hosting;

/// <summary>Composition root: wires shared building blocks and modules.</summary>
internal static class ApiHostingExtensions
{
    public static WebApplicationBuilder AddApi(this WebApplicationBuilder builder)
    {
        var product = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("product.json", optional: true)
            .Build()
            .Get<ProductInfo>() ?? new ProductInfo();
        builder.Services.AddSingleton(product);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProblemDetailsErrors();
        builder.Services.AddRequestValidation();
        builder.Services.AddApiOpenApi(product);
        builder.Services.AddHealthChecks();

        return builder;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi("/openapi/{documentName}.json");
            app.MapScalarApiReference("/docs", o => o.WithOpenApiRoutePattern("/openapi/{documentName}.json"));
        }

        app.MapApiHealth();

        app.MapGroup("/api")
            .AddEndpointFilterFactory(ValidationEndpointFilter.Create);

        return app;
    }
}
