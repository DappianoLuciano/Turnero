using Api.Modules.Businesses.Contracts;
using Api.Modules.Users.Contracts;
using Api.Shared.Auth;
using Api.Shared.Errors;

namespace Api.Hosting;

/// <summary>
/// Development data: <c>dotnet run --project src/Api -- seed</c>. Idempotent.
/// Uses only module contracts (no direct table access).
/// </summary>
internal static class DevSeed
{
    public const string Command = "seed";
    public const string OwnerEmail = "owner@demo.local";
    public const string OwnerPassword = "Demo1234!";
    public const string BusinessSlug = "demo";

    public static async Task RunAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("The dev seed only runs in Development.");
        }

        await using var scope = app.Services.CreateAsyncScope();
        var businesses = scope.ServiceProvider.GetRequiredService<IBusinessProvisioning>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<WebApplication>>();

        BusinessSummary business;
        try
        {
            business = await businesses.CreateAsync("Barbería Demo", BusinessSlug);
        }
        catch (AppException ex) when (ex.Code == "slug_taken")
        {
            logger.LogInformation("Seed already applied (business '{Slug}' exists).", BusinessSlug);
            return;
        }

        // Fresh scope: TenantScope requires no open transaction and a clean tenant context.
        await using var userScope = app.Services.CreateAsyncScope();
        var users = userScope.ServiceProvider.GetRequiredService<IUserProvisioning>();
        await users.AddMemberAsync(business.Id, OwnerEmail, "Dueño Demo", OwnerPassword, BusinessRole.Owner);

        logger.LogInformation("Seed created business '{Slug}' and owner {Email} / {Password}",
            BusinessSlug, OwnerEmail, OwnerPassword);
    }
}
