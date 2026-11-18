using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.CashRegister;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CashRegisterEndpointsTests(PostgresFixture db) : IAsyncLifetime
{
    private TwoTenants _t = null!;
    private HttpClient _client = null!;
    private string _token = null!;
    private string _today = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _t = await TwoTenants.CreateAsync(db);
        _client = db.Api.CreateApiClient();
        _token = await TokenAsync(_t.A.OwnerEmail);
        // Businesses default to Buenos Aires (UTC-3, no DST).
        _today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)).ToString("yyyy-MM-dd");
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task An_empty_day_expects_no_cash()
    {
        var day = await DayAsync(_token);

        day.GetProperty("expected").GetDecimal().ShouldBe(0m);
        day.GetProperty("isClosed").GetBoolean().ShouldBeFalse();
        day.GetProperty("movements").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Expected_cash_is_cash_payments_plus_manual_movements_and_ignores_other_methods()
    {
        await PayAsync("Cash", 3000m);
        await PayAsync("Card", 2000m);
        await MoveAsync(_token, "Income", 1000m, "Cambio inicial");
        await MoveAsync(_token, "Expense", 400m, "Insumos");

        var day = await DayAsync(_token);

        day.GetProperty("cashPayments").GetDecimal().ShouldBe(3000m);
        day.GetProperty("manualNet").GetDecimal().ShouldBe(600m);
        day.GetProperty("expected").GetDecimal().ShouldBe(3600m);
        var movements = day.GetProperty("movements").EnumerateArray().ToList();
        movements.Select(m => m.GetProperty("amount").GetDecimal()).ShouldBe([1000m, -400m]);
    }

    [Fact]
    public async Task Rejects_invalid_movements()
    {
        foreach (var body in new object[]
        {
            new { kind = "Income", amount = 0m, concept = "x" },
            new { kind = "Income", amount = -5m, concept = "x" },
            new { kind = "Income", amount = 1.005m, concept = "x" },
            new { kind = "Income", amount = 5m, concept = " " },
            new { kind = "Loan", amount = 5m, concept = "x" },
        })
        {
            var response = await SendAsync(HttpMethod.Post, "/api/cash-register/movements", _token, body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task A_reversal_undoes_the_movement_keeping_both_rows_and_only_once()
    {
        var id = await MoveAsync(_token, "Expense", 500m, "Error de carga");

        var reversal = await ReverseAsync(_token, id, "Monto equivocado");
        reversal.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await reversal.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("amount").GetDecimal().ShouldBe(500m);
        body.GetProperty("reversesId").GetGuid().ShouldBe(id);

        var day = await DayAsync(_token);
        day.GetProperty("movements").GetArrayLength().ShouldBe(2);
        day.GetProperty("manualNet").GetDecimal().ShouldBe(0m);

        var again = await ReverseAsync(_token, id, "Otra vez");
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(again)).ShouldBe("cash_movement_already_reversed");

        var reversalId = body.GetProperty("id").GetGuid();
        (await ErrorCodeAsync(await ReverseAsync(_token, reversalId, "No"))).ShouldBe("cash_movement_not_reversible");
    }

    [Fact]
    public async Task Reversing_needs_a_reason_and_the_owner_role_but_staff_can_register()
    {
        var staff = await TokenAsync($"staff-{_t.A.Slug}@test.local");
        var id = await MoveAsync(staff, "Income", 100m, "Propina");

        (await ReverseAsync(_token, id, " ")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReverseAsync(staff, id, "Error")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Closing_freezes_expected_counted_and_difference_and_blocks_the_day()
    {
        await PayAsync("Cash", 3000m);
        var expected = 3000m;

        var close = await CloseAsync(_token, _today, 2900m, "Faltan 100");
        close.StatusCode.ShouldBe(HttpStatusCode.Created);
        var closing = (await close.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("closing");
        closing.GetProperty("expected").GetDecimal().ShouldBe(expected);
        closing.GetProperty("counted").GetDecimal().ShouldBe(2900m);
        closing.GetProperty("difference").GetDecimal().ShouldBe(-100m);
        closing.GetProperty("note").GetString().ShouldBe("Faltan 100");

        (await DayAsync(_token)).GetProperty("isClosed").GetBoolean().ShouldBeTrue();

        var again = await CloseAsync(_token, _today, 3000m, null);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(again)).ShouldBe("cash_day_already_closed");

        var move = await SendAsync(HttpMethod.Post, "/api/cash-register/movements", _token,
            new { kind = "Income", amount = 10m, concept = "Tarde" });
        move.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(move)).ShouldBe("cash_day_closed");
    }

    [Fact]
    public async Task Cannot_close_a_future_day_or_with_a_negative_count()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)).ToString("yyyy-MM-dd");
        var response = await CloseAsync(_token, future, 0m, null);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).ShouldBe("cash_date_in_future");

        (await CloseAsync(_token, _today, -1m, null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Other_tenant_sees_an_empty_register_and_cannot_touch_the_movements()
    {
        var id = await MoveAsync(_token, "Income", 100m, "Cambio");
        await CloseAsync(_token, _today, 100m, null);
        var tokenB = await TokenAsync(_t.B.OwnerEmail);

        var day = await DayAsync(tokenB);
        day.GetProperty("movements").GetArrayLength().ShouldBe(0);
        day.GetProperty("isClosed").GetBoolean().ShouldBeFalse();
        (await ReverseAsync(tokenB, id, "Intento")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await SendAsync(HttpMethod.Get, $"/api/cash-register?date={_today}", null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    private Task<HttpResponseMessage> CloseAsync(string token, string date, decimal counted, string? note) =>
        SendAsync(HttpMethod.Post, "/api/cash-register/closings", token, new { date, countedAmount = counted, note });

    private Task<HttpResponseMessage> ReverseAsync(string token, Guid id, string reason) =>
        SendAsync(HttpMethod.Post, $"/api/cash-register/movements/{id}/reversal", token, new { reason });

    private async Task<Guid> MoveAsync(string token, string kind, decimal amount, string concept)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/cash-register/movements", token,
            new { kind, amount, concept });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> DayAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/cash-register?date={_today}", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    /// <summary>Books a 30-minute appointment priced 5000 and collects <paramref name="amount"/> on it.</summary>
    private async Task PayAsync(string method, decimal amount)
    {
        var customer = await CreateAsync("/api/customers", new { name = "Cliente " + Guid.NewGuid().ToString("N")[..6] });
        var service = await CreateAsync("/api/services", new { name = "Corte " + Guid.NewGuid().ToString("N")[..6], durationMinutes = 30, price = 5000m });
        var professional = await CreateAsync("/api/professionals",
            new { name = "Pro " + Guid.NewGuid().ToString("N")[..6], serviceIds = new[] { service } });
        var appointment = await CreateAsync("/api/appointments", new
        {
            customerId = customer,
            professionalId = professional,
            serviceId = service,
            startsAt = DateTimeOffset.UtcNow.AddDays(1),
            notes = (string?)null,
        });
        var response = await SendAsync(HttpMethod.Post, "/api/payments", _token,
            new { appointmentId = appointment, amount, method });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
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
