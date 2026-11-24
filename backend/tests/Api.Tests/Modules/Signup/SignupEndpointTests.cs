using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.Signup;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SignupEndpointTests(PostgresFixture db) : IAsyncLifetime
{
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _client = db.Api.CreateApiClient();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Signup_creates_business_owner_and_trial_and_the_owner_can_log_in()
    {
        var email = NewEmail();

        var response = await SignupAsync("Barbería Don José", email, "Passw0rd!");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("slug").GetString().ShouldStartWith("barberia-don-jose");

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!" }, Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = await login.Content.ReadFromJsonAsync<JsonElement>(Ct);
        session.GetProperty("user").GetProperty("role").GetString().ShouldBe("Owner");
        session.GetProperty("user").GetProperty("businessId").GetGuid().ShouldBe(body.GetProperty("businessId").GetGuid());

        var token = session.GetProperty("accessToken").GetString()!;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/subscription");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var sub = await (await _client.SendAsync(request, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        sub.GetProperty("state").GetString().ShouldBe("Trial");
    }

    [Fact]
    public async Task Two_businesses_with_the_same_name_get_different_slugs()
    {
        var first = await (await SignupAsync("Mismo Nombre", NewEmail(), "Passw0rd!")).Content
            .ReadFromJsonAsync<JsonElement>(Ct);
        var second = await (await SignupAsync("Mismo Nombre", NewEmail(), "Passw0rd!")).Content
            .ReadFromJsonAsync<JsonElement>(Ct);

        second.GetProperty("slug").GetString().ShouldNotBe(first.GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Signup_with_a_registered_email_is_rejected_and_does_not_touch_the_account()
    {
        var email = NewEmail();
        (await SignupAsync("Primero", email, "Passw0rd!")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var again = await SignupAsync("Segundo", email.ToUpperInvariant(), "OtraClave123");

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("email_taken");
        // The original password still works, the new one does not.
        (await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "OtraClave123" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("", "a@test.local", "Passw0rd!")]
    [InlineData("Negocio", "no-es-un-email", "Passw0rd!")]
    [InlineData("Negocio", "a@test.local", "corta")]
    public async Task Invalid_body_returns_validation_problem(string business, string email, string password)
    {
        var response = await SignupAsync(business, email, password);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("validation_failed");
    }

    private static string NewEmail() => $"signup-{Guid.NewGuid():N}@test.local";

    private Task<HttpResponseMessage> SignupAsync(string business, string email, string password) =>
        _client.PostAsJsonAsync("/api/signup",
            new { businessName = business, fullName = "Persona de Prueba", email, password }, Ct);
}
