using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.Customers;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CustomersEndpointsTests(PostgresFixture db) : IAsyncLifetime
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
    public async Task Owner_creates_gets_and_updates_a_customer_and_phone_is_normalized()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);

        var created = await SendAsync(HttpMethod.Post, "/api/customers", token,
            new { name = " Ana Pérez ", phone = "+54 9 (11) 5555-1234", email = "ana@mail.com", notes = " VIP " });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("name").GetString().ShouldBe("Ana Pérez");
        body.GetProperty("phone").GetString().ShouldBe("+5491155551234");
        body.GetProperty("notes").GetString().ShouldBe("VIP");
        body.GetProperty("isArchived").GetBoolean().ShouldBeFalse();
        var id = body.GetProperty("id").GetGuid();

        var updated = await SendAsync(HttpMethod.Put, $"/api/customers/{id}", token,
            new { name = "Ana P.", phone = (string?)null, email = (string?)null, notes = (string?)null, isArchived = false });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var got = await (await SendAsync(HttpMethod.Get, $"/api/customers/{id}", token)).Content
            .ReadFromJsonAsync<JsonElement>(Ct);
        got.GetProperty("name").GetString().ShouldBe("Ana P.");
        got.GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Staff_can_create_customers()
    {
        var token = await TokenAsync($"staff-{_t.A.Slug}@test.local");

        (await SendAsync(HttpMethod.Post, "/api/customers", token, new { name = "Cliente de staff" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Invalid_body_returns_validation_problem()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);

        var response = await SendAsync(HttpMethod.Post, "/api/customers", token,
            new { name = "", phone = "abc", email = "no-es-un-mail" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Duplicate_active_phone_is_rejected_but_archiving_frees_it()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);
        var first = await CreateAsync(token, "Primero", "11 4444-0000");

        var duplicate = await SendAsync(HttpMethod.Post, "/api/customers", token,
            new { name = "Segundo", phone = "1144440000" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("customer_phone_taken");

        (await SendAsync(HttpMethod.Put, $"/api/customers/{first}", token,
            new { name = "Primero", phone = "1144440000", isArchived = true })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Post, "/api/customers", token, new { name = "Segundo", phone = "1144440000" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Restoring the archived one now collides with the active customer holding the phone.
        (await SendAsync(HttpMethod.Put, $"/api/customers/{first}", token,
            new { name = "Primero", phone = "1144440000", isArchived = false })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Same_phone_is_allowed_in_different_businesses()
    {
        var tokenA = await TokenAsync(_t.A.OwnerEmail);
        var tokenB = await TokenAsync(_t.B.OwnerEmail);

        (await SendAsync(HttpMethod.Post, "/api/customers", tokenA, new { name = "En A", phone = "1155550000" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await SendAsync(HttpMethod.Post, "/api/customers", tokenB, new { name = "En B", phone = "1155550000" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task List_searches_paginates_and_hides_archived_by_default()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);
        await CreateAsync(token, "Bruno Díaz", "1100000001");
        await CreateAsync(token, "Berta Gómez", "1100000002");
        var archived = await CreateAsync(token, "Bea Archivada", "1100000003");
        await SendAsync(HttpMethod.Put, $"/api/customers/{archived}", token,
            new { name = "Bea Archivada", phone = "1100000003", isArchived = true });

        var all = await ListAsync(token, "");
        all.GetProperty("total").GetInt32().ShouldBe(2);
        all.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("name").GetString())
            .ShouldBe(["Berta Gómez", "Bruno Díaz"]);

        (await ListAsync(token, "?includeArchived=true")).GetProperty("total").GetInt32().ShouldBe(3);

        var byName = await ListAsync(token, "?search=bruno");
        byName.GetProperty("items").EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("Bruno Díaz");

        var byPhone = await ListAsync(token, "?search=11%2000000002");
        byPhone.GetProperty("items").EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("Berta Gómez");

        // "%" must be taken literally, not as a wildcard.
        (await ListAsync(token, "?search=%25")).GetProperty("total").GetInt32().ShouldBe(0);

        var page2 = await ListAsync(token, "?pageSize=1&page=2");
        page2.GetProperty("total").GetInt32().ShouldBe(2);
        page2.GetProperty("items").EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("Bruno Díaz");
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        (await SendAsync(HttpMethod.Get, "/api/customers", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Other_tenant_cannot_see_or_update_a_customer()
    {
        var tokenA = await TokenAsync(_t.A.OwnerEmail);
        var tokenB = await TokenAsync(_t.B.OwnerEmail);
        var id = await CreateAsync(tokenA, "Solo de A", "1122223333");

        (await ListAsync(tokenB, "")).GetProperty("items").EnumerateArray().ShouldBeEmpty();
        (await SendAsync(HttpMethod.Get, $"/api/customers/{id}", tokenB)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, $"/api/customers/{id}", tokenB,
            new { name = "Hackeado", isArchived = false })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Owner_deletes_a_customer()
    {
        var token = await TokenAsync(_t.A.OwnerEmail);
        var id = await CreateAsync(token, "Borrar", "1133334444");

        (await SendAsync(HttpMethod.Delete, $"/api/customers/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync(token, "?includeArchived=true")).GetProperty("items").EnumerateArray()
            .ShouldNotContain(c => c.GetProperty("id").GetGuid() == id);
        (await SendAsync(HttpMethod.Delete, $"/api/customers/{id}", token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_and_other_tenants_cannot_delete_a_customer()
    {
        var tokenA = await TokenAsync(_t.A.OwnerEmail);
        var id = await CreateAsync(tokenA, "Protegido", null);

        var staff = await TokenAsync($"staff-{_t.A.Slug}@test.local");
        (await SendAsync(HttpMethod.Delete, $"/api/customers/{id}", staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var tokenB = await TokenAsync(_t.B.OwnerEmail);
        (await SendAsync(HttpMethod.Delete, $"/api/customers/{id}", tokenB)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await SendAsync(HttpMethod.Get, $"/api/customers/{id}", tokenA)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateAsync(string token, string name, string? phone)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/customers", token, new { name, phone });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ListAsync(string token, string query)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/customers{query}", token);
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
