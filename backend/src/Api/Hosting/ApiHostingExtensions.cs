using Api.Modules;
using Api.Shared.Auth;
using Api.Shared.Errors;
using Api.Shared.Hosting;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
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
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddProblemDetailsErrors();
        builder.Services.AddRequestValidation();
        builder.Services.AddApiOpenApi(product);
        builder.Services.AddPersistence();
        builder.Services.AddAppAuthentication();
        builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");
        builder.Services.AddModules();

        return builder;
    }

    public static async Task<WebApplication> UseApiAsync(this WebApplication app)
    {
        if (!BuildTooling.IsOpenApiGeneration &&app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            var migrations = app.Configuration.GetConnectionString(PersistenceServiceCollectionExtensions.MigrationsConnectionName)
                ?? throw new InvalidOperationException("Missing connection string 'Migrations'.");
            await DatabaseMigrator.MigrateAsync(migrations);
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi("/openapi/{documentName}.json");
            app.MapScalarApiReference("/docs", o => o.WithOpenApiRoutePattern("/openapi/{documentName}.json"));
        }

        app.MapApiHealth();

        var api = app.MapGroup("/api").AddEndpointFilterFactory(ValidationEndpointFilter.Create);
        var groups = new ApiRouteGroups(
            Anonymous: api.MapGroup(string.Empty).AllowAnonymous(),
            Tenant: api.MapGroup(string.Empty)
                .RequireAuthorization(AuthPolicies.Member)
                .AddEndpointFilter<TenantTransactionFilter>());
        app.MapModules(groups);

        return app;
    }
}
