using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.Booking;

/// <summary>Customer self-service by link (view, reschedule, cancel), the notice policy and the reminders list.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PublicAppointmentSelfServiceTests(PostgresFixture db) : IAsyncLifetime
{
    private const string Phone = "+5491155551234";

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
    public async Task The_booking_returns_a_link_that_shows_the_appointment()
    {
        var token = await BookAsync(At(9));

        var response = await SendAsync(HttpMethod.Get, PublicPath(token), null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("serviceName").GetString().ShouldBe("Corte");
        body.GetProperty("professionalName").GetString().ShouldBe("Lucas");
        body.GetProperty("customerName").GetString().ShouldBe("Ana");
        body.GetProperty("status").GetString().ShouldBe("Scheduled");
        body.GetProperty("canChange").GetBoolean().ShouldBeTrue();
        body.GetProperty("noticeHours").GetInt32().ShouldBe(12);
        body.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(At(9));
    }

    [Fact]
    public async Task The_customer_reschedules_even_to_a_time_that_overlaps_their_own()
    {
        var token = await BookAsync(At(9));

        // 09:15 overlaps the current 09:00-09:30: it is offered because the appointment ignores itself.
        var offered = await SendAsync(HttpMethod.Get, $"{PublicPath(token)}/availability?date={_monday:yyyy-MM-dd}", null);
        offered.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await offered.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slots").EnumerateArray()
            .Select(s => s.GetProperty("startsAt").GetDateTimeOffset()).ShouldContain(At(9, 15));

        var moved = await SendAsync(HttpMethod.Post, $"{PublicPath(token)}/reschedule", null, new { startsAt = At(9, 15) });

        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await moved.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("startsAt").GetDateTimeOffset()
            .ShouldBe(At(9, 15));
        var agenda = await ListAgendaAsync();
        agenda.Select(a => a.GetProperty("startsAt").GetDateTimeOffset()).ShouldBe([At(9, 15)]);
    }

    [Fact]
    public async Task Rescheduling_to_a_time_that_is_not_offered_or_is_taken_is_refused()
    {
        var token = await BookAsync(At(9));
        await BookAsync(At(10), "Beto", "+5491166667777");

        var notOffered = await SendAsync(HttpMethod.Post, $"{PublicPath(token)}/reschedule", null, new { startsAt = At(14) });
        var taken = await SendAsync(HttpMethod.Post, $"{PublicPath(token)}/reschedule", null, new { startsAt = At(10) });

        notOffered.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(notOffered)).ShouldBe("slot_unavailable");
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(taken)).ShouldBe("slot_unavailable");
    }

    [Fact]
    public async Task The_customer_cancels_and_the_time_becomes_free_again()
    {
        var token = await BookAsync(At(9));

        var cancelled = await SendAsync(HttpMethod.Post, $"{PublicPath(token)}/cancel", null);

        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await cancelled.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("status").GetString().ShouldBe("Cancelled");
        body.GetProperty("canChange").GetBoolean().ShouldBeFalse();
        (await ListAgendaAsync()).Single().GetProperty("status").GetString().ShouldBe("Cancelled");

        var again = await SendAsync(HttpMethod.Post, $"{PublicPath(token)}/cancel", null);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(again)).ShouldBe("appointment_not_editable");
        (await SendAsync(HttpMethod.Post, $"{PublicPath(token)}/reschedule", null, new { startsAt = At(11) }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BookAsync(At(9), "Otra", "+5491100001111")).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Inside_the_notice_window_the_customer_cannot_change_it_online()
    {
        var token = await BookAsync(At(9));
        // The appointment is about a week away: 720 hours (30 days) of notice puts it inside the window.
        (await SendAsync(HttpMethod.Put, "/api/businesses/current/settings", _owner,
            new { phone = "+5491100009999", customerChangeNoticeHours = 720 })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var view = await (await SendAsync(HttpMethod.Get, PublicPath(token), null)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        view.GetProperty("canChange").GetBoolean().ShouldBeFalse();
        view.GetProperty("businessPhone").GetString().ShouldBe("+5491100009999");

        var cancel = await SendAsync(HttpMethod.Post, $"{PublicPath(token)}/cancel", null);
        cancel.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(cancel)).ShouldBe("change_window_closed");
        (await SendAsync(HttpMethod.Post, $"{PublicPath(token)}/reschedule", null, new { startsAt = At(11) }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ListAgendaAsync()).Single().GetProperty("status").GetString().ShouldBe("Scheduled");
    }

    [Fact]
    public async Task Invalid_forged_and_foreign_tokens_are_not_found()
    {
        var token = await BookAsync(At(9));
        var forged = token[..32] + new string('0', 32);
        var otherBusinessToken = await BookInBusinessBAsync();

        foreach (var bad in new[] { "no-es-un-token", forged, token[..^1] + (token[^1] == 'a' ? 'b' : 'a'), new string('a', 64) })
        {
            (await SendAsync(HttpMethod.Get, PublicPath(bad), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await SendAsync(HttpMethod.Post, $"{PublicPath(bad)}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // A valid token of business A does not open anything through business B's slug, and the other way around.
        (await SendAsync(HttpMethod.Get, $"/api/public/{_t.B.Slug}/appointments/{token}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, PublicPath(otherBusinessToken), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAgendaAsync()).Single().GetProperty("status").GetString().ShouldBe("Scheduled");
    }

    [Fact]
    public async Task Only_the_owner_edits_the_settings_and_they_are_validated()
    {
        var staff = await TokenAsync($"staff-{_t.A.Slug}@test.local");

        (await SendAsync(HttpMethod.Put, "/api/businesses/current/settings", staff,
            new { phone = (string?)null, customerChangeNoticeHours = 6 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, "/api/businesses/current/settings", _owner,
            new { phone = (string?)null, customerChangeNoticeHours = -1 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, "/api/businesses/current/settings", _owner,
            new { phone = (string?)null, customerChangeNoticeHours = 721 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await SendAsync(HttpMethod.Put, "/api/businesses/current/settings", _owner,
            new { phone = "  +5491100009999 ", customerChangeNoticeHours = 0 });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var current = await (await SendAsync(HttpMethod.Get, "/api/businesses/current", _owner)).Content
            .ReadFromJsonAsync<JsonElement>(Ct);
        current.GetProperty("phone").GetString().ShouldBe("+5491100009999");
        current.GetProperty("customerChangeNoticeHours").GetInt32().ShouldBe(0);
        var catalog = await (await SendAsync(HttpMethod.Get, $"/api/public/{_t.A.Slug}/catalog", null)).Content
            .ReadFromJsonAsync<JsonElement>(Ct);
        catalog.GetProperty("business").GetProperty("phone").GetString().ShouldBe("+5491100009999");
    }

    [Fact]
    public async Task Reminders_list_the_scheduled_appointments_of_the_day_with_phone_and_working_link()
    {
        var token = await BookAsync(At(9));
        var cancelledToken = await BookAsync(At(10), "Cancelada", "+5491122223333");
        await SendAsync(HttpMethod.Post, $"{PublicPath(cancelledToken)}/cancel", null);

        var response = await SendAsync(HttpMethod.Get, $"/api/appointments/reminders?date={_monday:yyyy-MM-dd}", _owner);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var item = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().Single();
        item.GetProperty("customerName").GetString().ShouldBe("Ana");
        item.GetProperty("customerPhone").GetString().ShouldBe(Phone);
        item.GetProperty("serviceName").GetString().ShouldBe("Corte");
        item.GetProperty("reminderSentAt").ValueKind.ShouldBe(JsonValueKind.Null);
        // The link the business sends is the same one the customer got when booking.
        item.GetProperty("manageToken").GetString().ShouldBe(token);
    }

    [Fact]
    public async Task Marking_a_reminder_sent_sticks_until_the_time_changes()
    {
        var token = await BookAsync(At(9));
        var id = (await ListAgendaAsync()).Single().GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Post, $"/api/appointments/{id}/reminder-sent", _owner)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await ReminderSentAtAsync()).ValueKind.ShouldNotBe(JsonValueKind.Null);

        (await SendAsync(HttpMethod.Post, $"{PublicPath(token)}/reschedule", null, new { startsAt = At(11) }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReminderSentAtAsync()).ValueKind.ShouldBe(JsonValueKind.Null);

        (await SendAsync(HttpMethod.Post, $"/api/appointments/{Guid.NewGuid()}/reminder-sent", _owner)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reminders_never_show_another_business_appointments()
    {
        await BookAsync(At(9));
        var tokenB = await TokenAsync(_t.B.OwnerEmail);

        var response = await SendAsync(HttpMethod.Get, $"/api/appointments/reminders?date={_monday:yyyy-MM-dd}", tokenB);

        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
    }

    private async Task<JsonElement> ReminderSentAtAsync()
    {
        var list = await (await SendAsync(HttpMethod.Get, $"/api/appointments/reminders?date={_monday:yyyy-MM-dd}", _owner))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        return list.EnumerateArray().Single().GetProperty("reminderSentAt").Clone();
    }

    private string PublicPath(string token) => $"/api/public/{_t.A.Slug}/appointments/{token}";

    private async Task<string> BookAsync(DateTimeOffset startsAt, string name = "Ana", string phone = Phone)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/public/{_t.A.Slug}/appointments", null, new
        {
            professionalId = _lucas,
            serviceId = _cut,
            startsAt,
            customerName = name,
            customerPhone = phone,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("manageToken").GetString()!;
    }

    private async Task<string> BookInBusinessBAsync()
    {
        var ownerB = await TokenAsync(_t.B.OwnerEmail);
        var service = await CreateAsync("/api/services", new { name = "Corte", durationMinutes = 30, price = 1m }, ownerB);
        var professional = await CreateAsync("/api/professionals", new { name = "Pepe", serviceIds = new[] { service } }, ownerB);
        (await SendAsync(HttpMethod.Put, $"/api/schedules/professionals/{professional}", ownerB,
            new { blocks = new[] { new { dayOfWeek = 1, startMinute = 540, endMinute = 720 } } }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var response = await SendAsync(HttpMethod.Post, $"/api/public/{_t.B.Slug}/appointments", null, new
        {
            professionalId = professional,
            serviceId = service,
            startsAt = At(9),
            customerName = "Zoe",
            customerPhone = "+5491100002222",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("manageToken").GetString()!;
    }

    private async Task<List<JsonElement>> ListAgendaAsync()
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/appointments?date={_monday:yyyy-MM-dd}", _owner);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().ToList();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

    private async Task<Guid> CreateAsync(string path, object body, string? token = null)
    {
        var response = await SendAsync(HttpMethod.Post, path, token ?? _owner, body);
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
