using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.Users;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AuthEndpointsTests(PostgresFixture db) : IAsyncLifetime
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
    public async Task Login_returns_access_token_and_sets_secure_refresh_cookie()
    {
        var response = await LoginAsync(_t.A.OwnerEmail, TwoTenants.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("accessToken").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("user").GetProperty("businessId").GetGuid().ShouldBe(_t.A.BusinessId);
        body.GetProperty("user").GetProperty("role").GetString().ShouldBe("Owner");

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("rt=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/api/auth", Case.Insensitive);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_problem_details()
    {
        var response = await LoginAsync(_t.A.OwnerEmail, "wrong-password");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("invalid_credentials");
        problem.GetProperty("status").GetInt32().ShouldBe(401);
    }

    [Fact]
    public async Task Login_with_unknown_email_returns_the_same_error()
    {
        var response = await LoginAsync("nobody@test.local", TwoTenants.Password);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("invalid_credentials");
    }

    [Fact]
    public async Task Login_with_invalid_body_returns_validation_problem()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "not-an-email", password = "" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        var errors = problem.GetProperty("errors");
        errors.GetProperty("email")[0].GetString().ShouldBe("invalid_email");
        errors.GetProperty("password")[0].GetString().ShouldBe("required");
    }

    [Fact]
    public async Task Access_token_gives_me_and_current_business_of_its_own_tenant_only()
    {
        var tokenA = await AccessTokenAsync(_t.A.OwnerEmail);
        var tokenB = await AccessTokenAsync(_t.B.OwnerEmail);

        var meA = await GetJsonAsync("/api/me", tokenA);
        meA.GetProperty("id").GetGuid().ShouldBe(_t.A.OwnerId);
        meA.GetProperty("businessId").GetGuid().ShouldBe(_t.A.BusinessId);

        (await GetJsonAsync("/api/businesses/current", tokenA)).GetProperty("id").GetGuid().ShouldBe(_t.A.BusinessId);
        (await GetJsonAsync("/api/businesses/current", tokenB)).GetProperty("id").GetGuid().ShouldBe(_t.B.BusinessId);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401_problem()
    {
        var response = await _client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Refresh_rotates_token_and_reuse_revokes_the_family()
    {
        var login = await LoginAsync(_t.A.OwnerEmail, TwoTenants.Password);
        var first = RefreshCookie(login);

        var refreshed = await RefreshAsync(first);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await refreshed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()
            .ShouldNotBeNullOrWhiteSpace();
        var second = RefreshCookie(refreshed);
        second.ShouldNotBe(first);

        // Reusing the rotated token = theft signal: rejected and the whole family is revoked.
        (await RefreshAsync(first)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RefreshAsync(second)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_without_cookie_is_rejected()
    {
        var response = await _client.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var token = RefreshCookie(await LoginAsync(_t.B.OwnerEmail, TwoTenants.Password));

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("Cookie", $"rt={token}");
        (await _client.SendAsync(logout, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RefreshAsync(token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_checks_the_database()
    {
        var response = await _client.GetAsync(new Uri("/health", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        body.GetProperty("checks").GetProperty("database").GetProperty("status").GetString().ShouldBe("Healthy");
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);

    private async Task<string> AccessTokenAsync(string email)
    {
        var response = await LoginAsync(email, TwoTenants.Password);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!;
    }

    private async Task<JsonElement> GetJsonAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"rt={refreshToken}");
        return await _client.SendAsync(request, Ct);
    }

    private static string RefreshCookie(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("rt=", StringComparison.Ordinal));
        return header[3..header.IndexOf(';', StringComparison.Ordinal)];
    }
}
