using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.Booking;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PublicBookingEndpointsTests(PostgresFixture db) : IAsyncLifetime
{
    private TwoTenants _t = null!;
    private HttpClient _client = null!;
    private string _owner = null!;
    private Guid _cut;
    private Guid _lucas;
    private DateOnly _monday;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Buenos Aires is UTC-3 all year (no DST).
    private DateTimeOffset At(int hour, int minute = 0) =>
        new(_monday.Year, _monday.Month, _monday.Day, hour, minute, 0, TimeSpan.FromHours(-3));

    public async ValueTask InitializeAsync()
    {
        _t = await TwoTenants.CreateAsync(db);
        _client = db.Api.CreateApiClient();
        _owner = await TokenAsync(_t.A.OwnerEmail);
        _cut = await CreateAsync("/api/services", new { name = "Corte", durationMinutes = 30, price = 8500m });
        _lucas = await CreateAsync("/api/professionals", new { name = "Lucas", serviceIds = new[] { _cut } });
        // A Monday in the future with a 09:00-12:00 shift.
        _monday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        while (_monday.DayOfWeek != DayOfWeek.Monday)
        {
            _monday = _monday.AddDays(1);
        }

        (await SendAsync(HttpMethod.Put, $"/api/schedules/professionals/{_lucas}", _owner,
            new { blocks = new[] { new { dayOfWeek = 1, startMinute = 540, endMinute = 720 } } }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task The_catalog_is_public_and_lists_only_what_can_be_booked()
    {
        var unused = await CreateAsync("/api/services", new { name = "Sin profesional", durationMinutes = 20, price = 1m });
        var inactive = await CreateAsync("/api/services", new { name = "Inactivo", durationMinutes = 20, price = 1m });
        await SendAsync(HttpMethod.Put, $"/api/services/{inactive}", _owner,
            new { name = "Inactivo", durationMinutes = 20, price = 1m, isActive = false });
        await CreateAsync("/api/professionals", new { name = "Sin servicios", serviceIds = Array.Empty<Guid>() });
        unused.ShouldNotBe(Guid.Empty);

        var response = await SendAsync(HttpMethod.Get, $"/api/public/{_t.A.Slug}/catalog", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("business").GetProperty("slug").GetString().ShouldBe(_t.A.Slug);
        body.GetProperty("business").GetProperty("timeZone").GetString().ShouldBe("America/Argentina/Buenos_Aires");
        body.GetProperty("services").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ShouldBe(["Corte"]);
        body.GetProperty("services")[0].GetProperty("price").GetDecimal().ShouldBe(8500m);
        body.GetProperty("professionals").EnumerateArray().Select(p => p.GetProperty("name").GetString())
            .ShouldBe(["Lucas"]);
    }

    [Fact]
    public async Task Each_slug_shows_only_its_own_business_and_unknown_slugs_are_not_found()
    {
        var other = await SendAsync(HttpMethod.Get, $"/api/public/{_t.B.Slug}/catalog", null);
        other.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await other.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("business").GetProperty("slug").GetString().ShouldBe(_t.B.Slug);
        body.GetProperty("services").GetArrayLength().ShouldBe(0);
        body.GetProperty("professionals").GetArrayLength().ShouldBe(0);

        foreach (var slug in new[] { "no-existe", "UPPER", "con_guion", "-raro", new string('a', 80) })
        {
            var response = await SendAsync(HttpMethod.Get, $"/api/public/{slug}/catalog", null);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ErrorCodeAsync(response)).ShouldBe("business_not_found");
        }
    }

    [Fact]
    public async Task Availability_offers_the_free_times_of_the_shift_without_logging_in()
    {
        var response = await AvailabilityAsync(_t.A.Slug, _lucas, _cut, _monday);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("isClosed").GetBoolean().ShouldBeFalse();
        // 09:00-12:00 with a 30-minute service every 15 minutes: 09:00..11:30 = 11 starts.
        body.GetProperty("slots").GetArrayLength().ShouldBe(11);
        body.GetProperty("slots")[0].GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(9));
    }

    [Fact]
    public async Task Availability_rejects_past_and_too_distant_dates_and_hides_other_businesses_professionals()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        var past = await AvailabilityAsync(_t.A.Slug, _lucas, _cut, yesterday);
        past.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(past)).ShouldBe("date_out_of_range");

        var far = await AvailabilityAsync(_t.A.Slug, _lucas, _cut, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(200)));
        far.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Lucas does not exist for business B.
        (await AvailabilityAsync(_t.B.Slug, _lucas, _cut, _monday)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Books_without_an_account_creating_the_customer_and_showing_the_appointment_in_the_panel()
    {
        var response = await BookAsync(_t.A.Slug, At(10), "Ana Pérez", "11 5555-1234");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("serviceName").GetString().ShouldBe("Corte");
        body.GetProperty("professionalName").GetString().ShouldBe("Lucas");
        body.GetProperty("customerName").GetString().ShouldBe("Ana Pérez");
        body.GetProperty("price").GetDecimal().ShouldBe(8500m);
        body.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(10));
        body.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(At(10, 30));

        var day = await SendAsync(HttpMethod.Get, $"/api/appointments?date={_monday:yyyy-MM-dd}", _owner);
        var appointment = (await day.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().Single();
        appointment.GetProperty("customerName").GetString().ShouldBe("Ana Pérez");
        appointment.GetProperty("status").GetString().ShouldBe("Scheduled");

        var customers = await SendAsync(HttpMethod.Get, "/api/customers?search=Ana", _owner);
        var items = (await customers.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items");
        items.GetArrayLength().ShouldBe(1);
        items[0].GetProperty("phone").GetString().ShouldBe("1155551234");
    }

    [Fact]
    public async Task A_returning_customer_is_matched_by_phone_and_keeps_the_name_the_business_has()
    {
        (await BookAsync(_t.A.Slug, At(9), "Ana Pérez", "1155551234")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var again = await BookAsync(_t.A.Slug, At(10), "Anita", "(11) 5555-1234");

        again.StatusCode.ShouldBe(HttpStatusCode.Created);
        var customers = await SendAsync(HttpMethod.Get, "/api/customers", _owner);
        var items = (await customers.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items");
        items.GetArrayLength().ShouldBe(1);
        items[0].GetProperty("name").GetString().ShouldBe("Ana Pérez");
    }

    [Fact]
    public async Task The_response_never_reveals_the_name_stored_for_a_phone()
    {
        (await BookAsync(_t.A.Slug, At(9), "Ana Pérez", "1155551234")).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Anyone who knows (or guesses) the phone must not learn whose it is.
        var probe = await BookAsync(_t.A.Slug, At(10), "  Quien Sea  ", "1155551234");

        probe.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await probe.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("customerName").GetString().ShouldBe("Quien Sea");
        body.GetRawText().ShouldNotContain("Ana");
    }

    [Fact]
    public async Task A_phone_cannot_hold_more_than_the_maximum_of_upcoming_appointments()
    {
        var max = 3;
        for (var i = 0; i < max; i++)
        {
            (await BookAsync(_t.A.Slug, At(9 + i), "Ana Pérez", "1155551234")).StatusCode
                .ShouldBe(HttpStatusCode.Created);
        }

        var extra = await BookAsync(_t.A.Slug, At(11, 30), "Ana Pérez", "1155551234");
        extra.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(extra)).ShouldBe("too_many_upcoming_appointments");

        // Nothing of the refused booking stays in the agenda, and other phones can still book.
        var day = await SendAsync(HttpMethod.Get, $"/api/appointments?date={_monday:yyyy-MM-dd}", _owner);
        (await day.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(max);
        (await BookAsync(_t.A.Slug, At(11, 30), "Beto Gómez", "1155559999")).StatusCode
            .ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_taken_time_is_refused_with_a_conflict_and_the_slot_disappears_from_availability()
    {
        (await BookAsync(_t.A.Slug, At(10), "Ana Pérez", "1155551234")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await BookAsync(_t.A.Slug, At(10), "Beto Gómez", "1155559999");
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(second)).ShouldBe("slot_unavailable");
        // An overlapping start is not offered either.
        (await BookAsync(_t.A.Slug, At(10, 15), "Beto Gómez", "1155559999")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var availability = await AvailabilityAsync(_t.A.Slug, _lucas, _cut, _monday);
        var starts = (await availability.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slots").EnumerateArray()
            .Select(s => s.GetProperty("startsAt").GetDateTimeOffset()).ToList();
        starts.ShouldNotContain(At(10));
        starts.ShouldContain(At(10, 30));
    }

    [Fact]
    public async Task Only_times_that_availability_offers_can_be_booked()
    {
        // Outside the shift, off the 15-minute grid, in the past, and on a day without hours.
        foreach (var startsAt in new[] { At(14), At(10, 7), At(9).AddDays(-30), At(10).AddDays(1) })
        {
            var response = await BookAsync(_t.A.Slug, startsAt, "Ana Pérez", "1155551234");
            response.StatusCode.ShouldBeOneOf(HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        }

        (await SendAsync(HttpMethod.Get, "/api/customers", _owner)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var customers = await SendAsync(HttpMethod.Get, "/api/customers", _owner);
        // Nothing was created by the refused attempts.
        (await customers.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_closed_day_cannot_be_booked()
    {
        await SendAsync(HttpMethod.Post, "/api/schedules/exceptions", _owner,
            new { date = _monday.ToString("yyyy-MM-dd"), professionalId = (Guid?)null, reason = "Feriado" });

        var response = await BookAsync(_t.A.Slug, At(10), "Ana Pérez", "1155551234");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).ShouldBe("slot_unavailable");
    }

    [Fact]
    public async Task Validates_the_customer_data()
    {
        var noName = await BookAsync(_t.A.Slug, At(10), " ", "1155551234");
        noName.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var badPhone = await BookAsync(_t.A.Slug, At(10), "Ana Pérez", "no es un telefono");
        badPhone.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(badPhone)).ShouldBe("invalid_phone");

        (await BookAsync(_t.A.Slug, At(10), "Ana Pérez", "")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cannot_book_a_professional_or_service_of_another_business()
    {
        var response = await BookAsync(_t.B.Slug, At(10), "Ana Pérez", "1155551234");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var day = await SendAsync(HttpMethod.Get, $"/api/appointments?date={_monday:yyyy-MM-dd}", _owner);
        (await day.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        var tokenB = await TokenAsync(_t.B.OwnerEmail);
        var customersB = await SendAsync(HttpMethod.Get, "/api/customers", tokenB);
        (await customersB.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_customer_with_the_same_phone_in_another_business_is_a_different_customer()
    {
        (await BookAsync(_t.A.Slug, At(10), "Ana Pérez", "1155551234")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var tokenB = await TokenAsync(_t.B.OwnerEmail);
        var customersB = await SendAsync(HttpMethod.Get, "/api/customers?search=Ana", tokenB);

        (await customersB.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    private Task<HttpResponseMessage> AvailabilityAsync(string slug, Guid professional, Guid service, DateOnly date) =>
        SendAsync(HttpMethod.Get,
            $"/api/public/{slug}/availability?professionalId={professional}&serviceId={service}&date={date:yyyy-MM-dd}", null);

    private Task<HttpResponseMessage> BookAsync(string slug, DateTimeOffset startsAt, string name, string phone) =>
        SendAsync(HttpMethod.Post, $"/api/public/{slug}/appointments", null, new
        {
            professionalId = _lucas,
            serviceId = _cut,
            startsAt,
            customerName = name,
            customerPhone = phone,
        });

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

    private async Task<Guid> CreateAsync(string path, object body)
    {
        var response = await SendAsync(HttpMethod.Post, path, _owner, body);
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
