using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.Schedules;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SchedulesEndpointsTests(PostgresFixture db) : IAsyncLifetime
{
    private TwoTenants _t = null!;
    private HttpClient _client = null!;
    private string _token = null!;
    private Guid _service;
    private Guid _professional;
    private DateOnly _monday;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _t = await TwoTenants.CreateAsync(db);
        _client = db.Api.CreateApiClient();
        _token = await TokenAsync(_t.A.OwnerEmail);
        _service = await CreateAsync("/api/services", new { name = "Corte", durationMinutes = 30, price = 8500m });
        _professional = await CreateAsync("/api/professionals", new { name = "Lucas", serviceIds = new[] { _service } });
        // A Monday in the future, so no slot is discarded for being in the past.
        _monday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        while (_monday.DayOfWeek != DayOfWeek.Monday)
        {
            _monday = _monday.AddDays(1);
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Replaces_the_weekly_schedule_and_lists_it_ordered()
    {
        var response = await PutScheduleAsync(_token, _professional,
            Block(2, 540, 720), Block(1, 840, 960), Block(1, 540, 720));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var blocks = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("blocks").EnumerateArray()
            .Select(b => (b.GetProperty("dayOfWeek").GetInt32(), b.GetProperty("startMinute").GetInt32())).ToList();
        blocks.ShouldBe([(1, 540), (1, 840), (2, 540)]);

        // Replacing again leaves only the new blocks.
        (await PutScheduleAsync(_token, _professional, Block(3, 600, 660))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var all = await SendAsync(HttpMethod.Get, "/api/schedules", _token);
        (await all.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("blocks").GetArrayLength().ShouldBe(1);

        // An empty list clears the schedule.
        (await PutScheduleAsync(_token, _professional)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rejects_overlapping_inverted_and_out_of_range_blocks_without_changing_the_schedule()
    {
        await PutScheduleAsync(_token, _professional, Block(1, 540, 720));

        foreach (var invalid in new[]
        {
            new[] { Block(1, 540, 720), Block(1, 700, 800) },
            new[] { Block(1, 700, 600) },
            new[] { Block(7, 540, 720) },
            new[] { Block(1, 540, 1441) },
            new[] { Block(1, -1, 60) },
        })
        {
            var response = await PutScheduleAsync(_token, _professional, invalid);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // Touching blocks are fine (half-open ranges).
        (await PutScheduleAsync(_token, _professional, Block(1, 540, 720), Block(1, 720, 840)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var original = await SendAsync(HttpMethod.Get, "/api/schedules", _token);
        (await original.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("blocks").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Only_the_owner_edits_but_every_member_reads()
    {
        var staff = await TokenAsync($"staff-{_t.A.Slug}@test.local");

        (await PutScheduleAsync(staff, _professional, Block(1, 540, 720))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/schedules", staff)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/schedules", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PutScheduleAsync(_token, Guid.CreateVersion7(), Block(1, 540, 720))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Creates_lists_and_deletes_closed_days_and_refuses_duplicates()
    {
        var created = await SendAsync(HttpMethod.Post, "/api/schedules/exceptions", _token,
            new { date = "2026-12-25", professionalId = (Guid?)null, reason = " Navidad " });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var duplicate = await SendAsync(HttpMethod.Post, "/api/schedules/exceptions", _token,
            new { date = "2026-12-25", professionalId = (Guid?)null, reason = "Otra vez" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(duplicate)).ShouldBe("schedule_exception_exists");

        // The same day for one professional is a different closure.
        (await SendAsync(HttpMethod.Post, "/api/schedules/exceptions", _token,
            new { date = "2026-12-25", professionalId = _professional, reason = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var list = await SendAsync(HttpMethod.Get, "/api/schedules/exceptions?from=2026-12-01&to=2026-12-31", _token);
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items.Select(i => i.GetProperty("reason").GetString()).ShouldContain("Navidad");

        (await SendAsync(HttpMethod.Delete, $"/api/schedules/exceptions/{id}", _token)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Delete, $"/api/schedules/exceptions/{id}", _token)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        (await SendAsync(HttpMethod.Get, "/api/schedules/exceptions?from=2026-12-31&to=2026-12-01", _token))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, "/api/schedules/exceptions", _token,
            new { date = "2026-12-24", professionalId = Guid.CreateVersion7(), reason = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Availability_offers_every_15_minutes_inside_the_blocks_while_the_service_fits()
    {
        // 09:00-12:00 and 14:00-16:00, 30-minute service: starts 09:00..11:30 (11) and 14:00..15:30 (7).
        await PutScheduleAsync(_token, _professional, Block(1, 540, 720), Block(1, 840, 960));

        var availability = await AvailabilityAsync(_token, _monday);

        availability.GetProperty("isClosed").GetBoolean().ShouldBeFalse();
        var slots = availability.GetProperty("slots").EnumerateArray().ToList();
        slots.Count.ShouldBe(18);
        slots[0].GetProperty("minutes").GetInt32().ShouldBe(540);
        // 09:00 local in Buenos Aires (UTC-3) is 12:00Z.
        slots[0].GetProperty("startsAt").GetDateTimeOffset().ShouldBe(
            new DateTimeOffset(_monday.Year, _monday.Month, _monday.Day, 9, 0, 0, TimeSpan.FromHours(-3)));
        slots.Last().GetProperty("minutes").GetInt32().ShouldBe(930);
    }

    [Fact]
    public async Task Availability_removes_the_times_taken_by_appointments_but_not_cancelled_ones()
    {
        await PutScheduleAsync(_token, _professional, Block(1, 540, 720));
        var customer = await CreateAsync("/api/customers", new { name = "Ana Pérez" });
        var ten = new DateTimeOffset(_monday.Year, _monday.Month, _monday.Day, 10, 0, 0, TimeSpan.FromHours(-3));
        var appointment = await CreateAsync("/api/appointments", new
        {
            customerId = customer,
            professionalId = _professional,
            serviceId = _service,
            startsAt = ten,
            notes = (string?)null,
        });

        var minutes = (await AvailabilityAsync(_token, _monday)).GetProperty("slots").EnumerateArray()
            .Select(s => s.GetProperty("minutes").GetInt32()).ToList();
        // 10:00-10:30 is taken: a 30-minute start at 09:45, 10:00 and 10:15 would overlap it.
        minutes.ShouldNotContain(585);
        minutes.ShouldNotContain(600);
        minutes.ShouldNotContain(615);
        minutes.ShouldContain(570);
        minutes.ShouldContain(630);

        await SendAsync(HttpMethod.Put, $"/api/appointments/{appointment}/status", _token, new { status = "Cancelled" });
        (await AvailabilityAsync(_token, _monday)).GetProperty("slots").EnumerateArray()
            .Select(s => s.GetProperty("minutes").GetInt32()).ShouldContain(600);
    }

    [Fact]
    public async Task Availability_is_empty_on_closed_days_and_days_without_hours()
    {
        await PutScheduleAsync(_token, _professional, Block(1, 540, 720));

        // Day without hours: not closed, simply no slots.
        var tuesday = await AvailabilityAsync(_token, _monday.AddDays(1));
        tuesday.GetProperty("isClosed").GetBoolean().ShouldBeFalse();
        tuesday.GetProperty("slots").GetArrayLength().ShouldBe(0);

        await SendAsync(HttpMethod.Post, "/api/schedules/exceptions", _token,
            new { date = _monday.ToString("yyyy-MM-dd"), professionalId = (Guid?)null, reason = "Feriado" });
        var closed = await AvailabilityAsync(_token, _monday);
        closed.GetProperty("isClosed").GetBoolean().ShouldBeTrue();
        closed.GetProperty("closedReason").GetString().ShouldBe("Feriado");
        closed.GetProperty("slots").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Availability_validates_the_professional_and_the_service()
    {
        var other = await CreateAsync("/api/services", new { name = "Barba", durationMinutes = 20, price = 5000m });

        var mismatch = await SendAsync(HttpMethod.Get,
            $"/api/availability?professionalId={_professional}&serviceId={other}&date={_monday:yyyy-MM-dd}", _token);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(mismatch)).ShouldBe("professional_service_mismatch");

        (await SendAsync(HttpMethod.Get,
            $"/api/availability?professionalId={Guid.CreateVersion7()}&serviceId={_service}&date={_monday:yyyy-MM-dd}", _token))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Other_tenant_sees_nothing_and_cannot_change_the_schedule()
    {
        await PutScheduleAsync(_token, _professional, Block(1, 540, 720));
        await SendAsync(HttpMethod.Post, "/api/schedules/exceptions", _token,
            new { date = "2026-12-25", professionalId = (Guid?)null, reason = "Navidad" });
        var tokenB = await TokenAsync(_t.B.OwnerEmail);

        var schedules = await SendAsync(HttpMethod.Get, "/api/schedules", tokenB);
        (await schedules.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("blocks").GetArrayLength().ShouldBe(0);
        var exceptions = await SendAsync(HttpMethod.Get, "/api/schedules/exceptions?from=2026-12-01&to=2026-12-31", tokenB);
        (await exceptions.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        (await PutScheduleAsync(tokenB, _professional, Block(1, 540, 720))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static object Block(int dayOfWeek, int startMinute, int endMinute) =>
        new { dayOfWeek, startMinute, endMinute };

    private Task<HttpResponseMessage> PutScheduleAsync(string token, Guid professional, params object[] blocks) =>
        SendAsync(HttpMethod.Put, $"/api/schedules/professionals/{professional}", token, new { blocks });

    private async Task<JsonElement> AvailabilityAsync(string token, DateOnly date)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"/api/availability?professionalId={_professional}&serviceId={_service}&date={date:yyyy-MM-dd}", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

    private async Task<Guid> CreateAsync(string path, object body)
    {
        var response = await SendAsync(HttpMethod.Post, path, _token, body);
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
