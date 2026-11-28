using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Api.Tests.Infrastructure;

/// <summary>The real API wired to the test container through the runtime role.</summary>
public sealed class ApiFactory(PostgresFixture db) : WebApplicationFactory<Program>
{
    public const string SigningKey = "test-signing-key-that-is-long-enough-for-hs256!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:App"] = db.RuntimeConnectionString,
            ["ConnectionStrings:Migrations"] = db.MigratorConnectionString,
            ["Database:ApplyMigrationsOnStartup"] = "false",
            ["Jwt:SigningKey"] = SigningKey,
            ["AppointmentLinks:SigningKey"] = "test-appointment-links-key-long-enough-for-hmac!",
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience",
            ["RateLimiting:SignupsPerHour"] = "10000",
            ["RateLimiting:PublicBookingsPerHour"] = "100000",
            ["RateLimiting:PublicPerMinute"] = "100000",
        }));
    }

    /// <summary>Client that does not manage cookies: tests send/inspect the refresh cookie explicitly.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });
}
