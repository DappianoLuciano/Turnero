using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.Staff;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class StaffEndpointsTests(PostgresFixture db) : IAsyncLifetime
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
    public async Task Owner_creates_lists_and_updates_a_professional_with_services()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);
        var cut = await CreateServiceAsync(token, "Corte");
        var beard = await CreateServiceAsync(token, "Barba");

        var created = await SendAsync(HttpMethod.Post, "/api/professionals", token,
            new { name = " Lucas ", serviceIds = new[] { cut, cut } });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("name").GetString().ShouldBe("Lucas");
        body.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        body.GetProperty("serviceIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([cut]);
        var id = body.GetProperty("id").GetGuid();

        var updated = await SendAsync(HttpMethod.Put, $"/api/professionals/{id}", token,
            new { name = "Lucas M.", isActive = false, serviceIds = new[] { beard } });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await ListAsync(token);
        var item = list.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);
        item.GetProperty("name").GetString().ShouldBe("Lucas M.");
        item.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        item.GetProperty("serviceIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([beard]);
    }

    [Fact]
    public async Task Unknown_or_foreign_service_is_rejected()
    {
        var tokenA = await TokenAsync(_t.A.OwnerEmail);
        var tokenB = await TokenAsync(_t.B.OwnerEmail);
        var serviceOfB = await CreateServiceAsync(tokenB, "Solo de B");

        var response = await SendAsync(HttpMethod.Post, "/api/professionals", tokenA,
            new { name = "Lucas", serviceIds = new[] { serviceOfB } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("service_not_found");
        (await ListAsync(tokenA)).EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_body_returns_validation_problem()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);

        var response = await SendAsync(HttpMethod.Post, "/api/professionals", token, new { name = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Staff_can_list_but_not_create_update_or_delete()
    {
        var owner = await TokenAsync(_t.A.OwnerEmail);
        var id = await CreateProfessionalAsync(owner, "Lucas");
        var staff = await TokenAsync($"staff-{_t.A.Slug}@test.local");

        (await SendAsync(HttpMethod.Get, "/api/professionals", staff)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, "/api/professionals", staff, new { name = "X", serviceIds = Array.Empty<Guid>() }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, $"/api/professionals/{id}", staff,
            new { name = "X", isActive = true, serviceIds = Array.Empty<Guid>() })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, $"/api/professionals/{id}", staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        (await SendAsync(HttpMethod.Get, "/api/professionals", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Other_tenant_cannot_see_update_or_delete_a_professional()
    {
        var tokenA = await TokenAsync(_t.A.OwnerEmail);
        var tokenB = await TokenAsync(_t.B.OwnerEmail);
        var id = await CreateProfessionalAsync(tokenA, "Solo de A");

        (await ListAsync(tokenB)).EnumerateArray().ShouldBeEmpty();
        (await SendAsync(HttpMethod.Put, $"/api/professionals/{id}", tokenB,
            new { name = "Hackeado", isActive = true, serviceIds = Array.Empty<Guid>() }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"/api/professionals/{id}", tokenB)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Owner_deletes_a_professional_and_deleting_a_service_unlinks_it()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);
        var service = await CreateServiceAsync(token, "Corte");
        var professional = await CreateProfessionalAsync(token, "Lucas", service);

        (await SendAsync(HttpMethod.Delete, $"/api/services/{service}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterServiceDelete = (await ListAsync(token)).EnumerateArray().Single();
        afterServiceDelete.GetProperty("serviceIds").EnumerateArray().ShouldBeEmpty();

        (await SendAsync(HttpMethod.Delete, $"/api/professionals/{professional}", token))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(token)).EnumerateArray().ShouldBeEmpty();
        (await SendAsync(HttpMethod.Delete, $"/api/professionals/{professional}", token))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_can_hide_inactive_professionals()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);
        await CreateProfessionalAsync(token, "Activo");
        var inactive = await CreateProfessionalAsync(token, "Inactivo");
        await SendAsync(HttpMethod.Put, $"/api/professionals/{inactive}", token,
            new { name = "Inactivo", isActive = false, serviceIds = Array.Empty<Guid>() });

        (await ListAsync(token)).GetArrayLength().ShouldBe(2);
        var active = await (await SendAsync(HttpMethod.Get, "/api/professionals?includeInactive=false", token)).Content
            .ReadFromJsonAsync<JsonElement>(Ct);
        active.EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("Activo");
    }

    private async Task<Guid> CreateServiceAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/services", token,
            new { name, durationMinutes = 30, price = 100m });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateProfessionalAsync(string token, string name, params Guid[] serviceIds)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/professionals", token, new { name, serviceIds });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ListAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/professionals", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
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
