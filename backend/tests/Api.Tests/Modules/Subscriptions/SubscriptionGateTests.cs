using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Modules.Subscriptions.Contracts;
using Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Api.Tests.Modules.Subscriptions;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SubscriptionGateTests(PostgresFixture db) : IAsyncLifetime
{
    private TwoTenants _t = null!;
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _t = await TwoTenants.CreateAsync(db);
        _client = db.Api.CreateApiClient();
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Every_new_business_starts_with_a_two_day_trial()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);

        var response = await GetAsync("/api/subscription", token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("state").GetString().ShouldBe("Trial");
        body.GetProperty("daysLeft").GetInt32().ShouldBe(2);
        (await GetAsync("/api/services", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Expired_trial_locks_panel_and_public_site_but_not_the_status_endpoints()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);
        await ExpireAsync(_t.A.BusinessId);

        var panel = await GetAsync("/api/services", token);
        panel.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await panel.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("subscription_required");
        (await GetAsync($"/api/public/{_t.A.Slug}/catalog", null)).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);

        var status = await GetAsync("/api/subscription", token);
        status.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await status.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("state").GetString().ShouldBe("Expired");
        (await GetAsync("/api/me", token)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Login keeps working so the owner can reach the "pay to continue" screen.
        _ = await TokenAsync(_t.A.OwnerEmail);
    }

    [Fact]
    public async Task Locking_one_business_does_not_affect_another()
    {
        var tokenB = await TokenAsync(_t.B.OwnerEmail);
        await ExpireAsync(_t.A.BusinessId);

        (await GetAsync("/api/services", tokenB)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync($"/api/public/{_t.B.Slug}/catalog", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Registering_a_payment_unlocks_the_business()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);
        await ExpireAsync(_t.A.BusinessId);
        (await GetAsync("/api/services", token)).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);

        SubscriptionSummary summary;
        await using (var scope = db.Api.Services.CreateAsyncScope())
        {
            summary = await scope.ServiceProvider.GetRequiredService<ISubscriptionAdmin>()
                .ActivateAsync(_t.A.BusinessId, 30, Ct);
        }

        summary.State.ShouldBe(SubscriptionState.Active);
        summary.DaysLeft.ShouldBe(30);
        (await GetAsync("/api/services", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync($"/api/public/{_t.A.Slug}/catalog", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Business_without_subscription_row_is_locked()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);
        await ExecuteAsync("DELETE FROM subscriptions WHERE business_id = @b", _t.A.BusinessId);

        (await GetAsync("/api/services", token)).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
    }

    private Task ExpireAsync(Guid businessId) => ExecuteAsync(
        "UPDATE subscriptions SET trial_ends_at = now() - interval '1 day', paid_until = NULL WHERE business_id = @b",
        businessId);

    private async Task ExecuteAsync(string sql, Guid businessId)
    {
        await using var connection = new NpgsqlConnection(db.SuperuserConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("b", businessId);
        (await command.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
    }

    private async Task<string> TokenAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password = TwoTenants.Password }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> GetAsync(string path, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return _client.SendAsync(request, Ct);
    }
}
