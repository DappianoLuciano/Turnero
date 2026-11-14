using System.Reflection;
using Api.Shared.Errors;
using Api.Shared.Hosting;
using Api.Shared.Persistence;
using Api.Shared.Validation;
using Scalar.AspNetCore;

namespace Api.Hosting;

/// <summary>Composition root: wires shared building blocks and modules.</summary>
internal static class ApiHostingExtensions
{
    /// <summary>True when the app is being started by the build-time OpenAPI generator.</summary>
    public static bool IsOpenApiGeneration =>
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

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
        builder.Services.AddPersistence();
        builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

        return builder;
    }

    public static async Task<WebApplication> UseApiAsync(this WebApplication app)
    {
        if (!IsOpenApiGeneration && app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            var migrations = app.Configuration.GetConnectionString(PersistenceServiceCollectionExtensions.MigrationsConnectionName)
                ?? throw new InvalidOperationException("Missing connection string 'Migrations'.");
            await DatabaseMigrator.MigrateAsync(migrations);
        }

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
