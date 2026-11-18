using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.Appointments;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AppointmentsEndpointsTests(PostgresFixture db) : IAsyncLifetime
{
    // Buenos Aires is UTC-3 all year (no DST): 10:00 local is 13:00Z.
    private static readonly DateTimeOffset Ten = new(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(-3));

    private TwoTenants _t = null!;
    private HttpClient _client = null!;
    private string _token = null!;
    private Guid _customer;
    private Guid _professional;
    private Guid _service;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _t = await TwoTenants.CreateAsync(db);
        _client = db.Api.CreateApiClient();
        _token = await TokenAsync(_t.A.OwnerEmail);
        _customer = await CreateAsync("/api/customers", _token, new { name = "Ana Pérez", phone = "1155550000" });
        _service = await CreateAsync("/api/services", _token, new { name = "Corte", durationMinutes = 30, price = 8500m });
        _professional = await CreateAsync("/api/professionals", _token, new { name = "Lucas", serviceIds = new[] { _service } });
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Creates_an_appointment_with_end_time_price_and_names_from_the_service()
    {
        var response = await BookAsync(_token, Ten, notes: " Primera vez ");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(Ten);
        body.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(Ten.AddMinutes(30));
        body.GetProperty("status").GetString().ShouldBe("Scheduled");
        body.GetProperty("price").GetDecimal().ShouldBe(8500m);
        body.GetProperty("customerName").GetString().ShouldBe("Ana Pérez");
        body.GetProperty("professionalName").GetString().ShouldBe("Lucas");
        body.GetProperty("serviceName").GetString().ShouldBe("Corte");
        body.GetProperty("notes").GetString().ShouldBe("Primera vez");
    }

    [Fact]
    public async Task Overlapping_appointments_of_the_same_professional_are_rejected_but_adjacent_ones_are_allowed()
    {
        (await BookAsync(_token, Ten)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var overlap = await BookAsync(_token, Ten.AddMinutes(15));
        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await overlap.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("appointment_overlap");

        (await BookAsync(_token, Ten.AddMinutes(30))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await BookAsync(_token, Ten.AddMinutes(-30))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Different_professionals_can_have_appointments_at_the_same_time()
    {
        var other = await CreateAsync("/api/professionals", _token, new { name = "Mateo", serviceIds = new[] { _service } });

        (await BookAsync(_token, Ten)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await BookAsync(_token, Ten, professional: other)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Cancelling_frees_the_slot_and_reopening_checks_it_again()
    {
        var first = await IdOfAsync(await BookAsync(_token, Ten));

        (await SetStatusAsync(first, "Cancelled")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await IdOfAsync(await BookAsync(_token, Ten));

        var reopen = await SetStatusAsync(first, "Scheduled");
        reopen.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await reopen.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("appointment_overlap");

        (await SetStatusAsync(second, "NoShow")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SetStatusAsync(first, "Completed")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reschedules_a_scheduled_appointment_and_rejects_editing_a_closed_one()
    {
        var id = await IdOfAsync(await BookAsync(_token, Ten));
        var moved = await SendAsync(HttpMethod.Put, $"/api/appointments/{id}", _token,
            new { professionalId = _professional, serviceId = _service, startsAt = Ten.AddHours(2), notes = (string?)null });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await moved.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("endsAt").GetDateTimeOffset()
            .ShouldBe(Ten.AddHours(2).AddMinutes(30));

        // Moving onto itself is not an overlap with itself.
        (await SendAsync(HttpMethod.Put, $"/api/appointments/{id}", _token,
            new { professionalId = _professional, serviceId = _service, startsAt = Ten.AddHours(2).AddMinutes(10), notes = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetStatusAsync(id, "Completed");
        var closed = await SendAsync(HttpMethod.Put, $"/api/appointments/{id}", _token,
            new { professionalId = _professional, serviceId = _service, startsAt = Ten, notes = (string?)null });
        closed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await closed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("appointment_not_editable");
    }

    [Fact]
    public async Task The_price_is_a_snapshot_of_the_service_when_it_was_booked()
    {
        var id = await IdOfAsync(await BookAsync(_token, Ten));
        await SendAsync(HttpMethod.Put, $"/api/services/{_service}", _token,
            new { name = "Corte", durationMinutes = 45, price = 12000m, isActive = true });

        var list = await ListAsync(_token, "2026-11-18");
        var item = list.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == id);
        item.GetProperty("price").GetDecimal().ShouldBe(8500m);
        item.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(Ten.AddMinutes(30));
    }

    [Fact]
    public async Task Rejects_professional_that_does_not_perform_the_service_and_inactive_ones()
    {
        var otherService = await CreateAsync("/api/services", _token, new { name = "Barba", durationMinutes = 20, price = 5000m });
        var mismatch = await BookAsync(_token, Ten, service: otherService);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await mismatch.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("professional_service_mismatch");

        await SendAsync(HttpMethod.Put, $"/api/professionals/{_professional}", _token,
            new { name = "Lucas", isActive = false, serviceIds = new[] { _service } });
        var inactive = await BookAsync(_token, Ten);
        inactive.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await inactive.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("professional_inactive");
    }

    [Fact]
    public async Task Rejects_inactive_service_and_archived_or_unknown_customers()
    {
        await SendAsync(HttpMethod.Put, $"/api/services/{_service}", _token,
            new { name = "Corte", durationMinutes = 30, price = 8500m, isActive = false });
        (await ErrorCodeAsync(await BookAsync(_token, Ten))).ShouldBe("service_inactive");
        await SendAsync(HttpMethod.Put, $"/api/services/{_service}", _token,
            new { name = "Corte", durationMinutes = 30, price = 8500m, isActive = true });

        (await ErrorCodeAsync(await BookAsync(_token, Ten, customer: Guid.CreateVersion7()))).ShouldBe("customer_not_found");

        await SendAsync(HttpMethod.Put, $"/api/customers/{_customer}", _token,
            new { name = "Ana Pérez", phone = "1155550000", isArchived = true });
        (await ErrorCodeAsync(await BookAsync(_token, Ten))).ShouldBe("customer_archived");
    }

    [Fact]
    public async Task Lists_the_local_day_of_the_business_and_can_filter_by_professional()
    {
        var other = await CreateAsync("/api/professionals", _token, new { name = "Mateo", serviceIds = new[] { _service } });
        await BookAsync(_token, new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.FromHours(-3)));
        await BookAsync(_token, new DateTimeOffset(2026, 11, 18, 23, 30, 0, TimeSpan.FromHours(-3)), professional: other);
        await BookAsync(_token, new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.FromHours(-3)));
        await BookAsync(_token, new DateTimeOffset(2026, 11, 17, 23, 30, 0, TimeSpan.FromHours(-3)));

        var day = await ListAsync(_token, "2026-11-18");
        day.GetArrayLength().ShouldBe(2);
        day.EnumerateArray().Select(a => a.GetProperty("startsAt").GetDateTimeOffset())
            .ShouldBe([new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.FromHours(-3)),
                new DateTimeOffset(2026, 11, 18, 23, 30, 0, TimeSpan.FromHours(-3))]);

        var onlyOther = await ListAsync(_token, $"2026-11-18&professionalId={other}");
        onlyOther.EnumerateArray().Single().GetProperty("professionalName").GetString().ShouldBe("Mateo");
    }

    [Fact]
    public async Task Invalid_body_returns_validation_problem()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/appointments", _token,
            new { customerId = Guid.Empty, professionalId = Guid.Empty, serviceId = Guid.Empty, startsAt = default(DateTimeOffset) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Staff_can_book_and_anonymous_cannot()
    {
        var staff = await TokenAsync($"staff-{_t.A.Slug}@test.local");

        (await BookAsync(staff, Ten)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await SendAsync(HttpMethod.Get, "/api/appointments?date=2026-11-18", null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Other_tenant_cannot_see_use_or_change_the_appointments()
    {
        var id = await IdOfAsync(await BookAsync(_token, Ten));
        var tokenB = await TokenAsync(_t.B.OwnerEmail);

        (await ListAsync(tokenB, "2026-11-18")).EnumerateArray().ShouldBeEmpty();
        (await SetStatusAsync(id, "Cancelled", tokenB)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, $"/api/appointments/{id}", tokenB,
            new { professionalId = _professional, serviceId = _service, startsAt = Ten, notes = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Booking with the ids of another business does not leak them: they simply do not exist for B.
        (await ErrorCodeAsync(await BookAsync(tokenB, Ten))).ShouldBe("customer_not_found");
    }

    [Fact]
    public async Task Customer_professional_and_service_with_appointments_cannot_be_deleted()
    {
        await BookAsync(_token, Ten);

        var customer = await SendAsync(HttpMethod.Delete, $"/api/customers/{_customer}", _token);
        customer.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(customer)).ShouldBe("customer_has_history");

        var professional = await SendAsync(HttpMethod.Delete, $"/api/professionals/{_professional}", _token);
        professional.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(professional)).ShouldBe("professional_has_history");

        var service = await SendAsync(HttpMethod.Delete, $"/api/services/{_service}", _token);
        service.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(service)).ShouldBe("service_has_history");

        // Still all there afterwards.
        (await SendAsync(HttpMethod.Get, $"/api/customers/{_customer}", _token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> BookAsync(
        string token, DateTimeOffset startsAt, Guid? customer = null, Guid? professional = null, Guid? service = null,
        string? notes = null) =>
        SendAsync(HttpMethod.Post, "/api/appointments", token, new
        {
            customerId = customer ?? _customer,
            professionalId = professional ?? _professional,
            serviceId = service ?? _service,
            startsAt,
            notes,
        });

    private Task<HttpResponseMessage> SetStatusAsync(Guid id, string status, string? token = null) =>
        SendAsync(HttpMethod.Put, $"/api/appointments/{id}/status", token ?? _token, new { status });

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

    private async Task<JsonElement> ListAsync(string token, string dateAndFilters)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/appointments?date={dateAndFilters}", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task<Guid> CreateAsync(string path, string token, object body)
    {
        var response = await SendAsync(HttpMethod.Post, path, token, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
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
