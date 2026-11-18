using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.Services;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ServicesEndpointsTests(PostgresFixture db) : IAsyncLifetime
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
    public async Task Owner_creates_lists_and_updates_a_service()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);

        var created = await SendAsync(HttpMethod.Post, "/api/services", token,
            new { name = "Corte", description = " Clásico ", durationMinutes = 30, price = 8500.50m });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("name").GetString().ShouldBe("Corte");
        body.GetProperty("description").GetString().ShouldBe("Clásico");
        body.GetProperty("currency").GetString().ShouldBe("ARS");
        body.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        var id = body.GetProperty("id").GetGuid();

        var updated = await SendAsync(HttpMethod.Put, $"/api/services/{id}", token,
            new { name = "Corte + barba", description = (string?)null, durationMinutes = 45, price = 11000m, isActive = false });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await (await SendAsync(HttpMethod.Get, "/api/services", token)).Content
            .ReadFromJsonAsync<JsonElement>(Ct);
        var item = list.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == id);
        item.GetProperty("name").GetString().ShouldBe("Corte + barba");
        item.GetProperty("durationMinutes").GetInt32().ShouldBe(45);
        item.GetProperty("price").GetDecimal().ShouldBe(11000m);
        item.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Invalid_body_returns_validation_problem()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);

        var response = await SendAsync(HttpMethod.Post, "/api/services", token,
            new { name = "", durationMinutes = 1, price = -5m });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Price_with_more_than_two_decimals_is_rejected()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);

        var response = await SendAsync(HttpMethod.Post, "/api/services", token,
            new { name = "Corte", durationMinutes = 30, price = 10.999m });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Staff_can_list_but_not_create()
    {
        var token = await TokenAsync($"staff-{_t.A.Slug}@test.local");

        (await SendAsync(HttpMethod.Get, "/api/services", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, "/api/services", token,
            new { name = "Corte", durationMinutes = 30, price = 100m })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        (await SendAsync(HttpMethod.Get, "/api/services", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Other_tenant_cannot_see_or_update_a_service()
    {
        var tokenA = await TokenAsync(_t.A.OwnerEmail);
        var tokenB = await TokenAsync(_t.B.OwnerEmail);
        var created = await SendAsync(HttpMethod.Post, "/api/services", tokenA,
            new { name = "Solo de A", durationMinutes = 30, price = 100m });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var listB = await (await SendAsync(HttpMethod.Get, "/api/services", tokenB)).Content
            .ReadFromJsonAsync<JsonElement>(Ct);
        listB.EnumerateArray().ShouldBeEmpty();

        var update = await SendAsync(HttpMethod.Put, $"/api/services/{id}", tokenB,
            new { name = "Hackeado", durationMinutes = 30, price = 1m, isActive = true });
        update.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Owner_deletes_a_service()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);
        var created = await SendAsync(HttpMethod.Post, "/api/services", token,
            new { name = "Borrar", durationMinutes = 30, price = 100m });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Delete, $"/api/services/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await (await SendAsync(HttpMethod.Get, "/api/services", token)).Content
            .ReadFromJsonAsync<JsonElement>(Ct);
        list.EnumerateArray().ShouldNotContain(s => s.GetProperty("id").GetGuid() == id);
        (await SendAsync(HttpMethod.Delete, $"/api/services/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_and_other_tenants_cannot_delete_a_service()
    {
        var tokenA = await TokenAsync(_t.A.OwnerEmail);
        var created = await SendAsync(HttpMethod.Post, "/api/services", tokenA,
            new { name = "Protegido", durationMinutes = 30, price = 100m });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var staff = await TokenAsync($"staff-{_t.A.Slug}@test.local");
        (await SendAsync(HttpMethod.Delete, $"/api/services/{id}", staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var tokenB = await TokenAsync(_t.B.OwnerEmail);
        (await SendAsync(HttpMethod.Delete, $"/api/services/{id}", tokenB)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await (await SendAsync(HttpMethod.Get, "/api/services", tokenA)).Content
            .ReadFromJsonAsync<JsonElement>(Ct);
        list.EnumerateArray().ShouldContain(s => s.GetProperty("id").GetGuid() == id);
    }

    private async Task<string> TokenAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password = TwoTenants.Password }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client.SendAsync(request, Ct);
    }
}
