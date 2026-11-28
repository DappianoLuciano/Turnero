using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.Payments;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PaymentsEndpointsTests(PostgresFixture db) : IAsyncLifetime
{
    // Buenos Aires is UTC-3 all year (no DST).
    private static readonly DateTimeOffset Ten = new(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(-3));

    private TwoTenants _t = null!;
    private HttpClient _client = null!;
    private string _token = null!;
    private Guid _appointment;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _t = await TwoTenants.CreateAsync(db);
        _client = db.Api.CreateApiClient();
        _token = await TokenAsync(_t.A.OwnerEmail);
        var customer = await CreateAsync("/api/customers", new { name = "Ana Pérez", phone = "1155550000" });
        var service = await CreateAsync("/api/services", new { name = "Corte", durationMinutes = 30, price = 8500m });
        var professional = await CreateAsync("/api/professionals", new { name = "Lucas", serviceIds = new[] { service } });
        _appointment = await CreateAsync("/api/appointments", new
        {
            customerId = customer,
            professionalId = professional,
            serviceId = service,
            startsAt = Ten,
            notes = (string?)null,
        });
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_new_appointment_has_no_payments_and_owes_its_price()
    {
        var summary = await SummaryAsync(_token);

        summary.GetProperty("price").GetDecimal().ShouldBe(8500m);
        summary.GetProperty("paid").GetDecimal().ShouldBe(0m);
        summary.GetProperty("balance").GetDecimal().ShouldBe(8500m);
        summary.GetProperty("payments").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Collects_partial_payments_until_the_balance_is_zero()
    {
        var first = await PayAsync(_token, 3000m, "Cash");
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("paid").GetDecimal().ShouldBe(3000m);
        body.GetProperty("balance").GetDecimal().ShouldBe(5500m);

        (await PayAsync(_token, 5500m, "Transfer")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var summary = await SummaryAsync(_token);
        summary.GetProperty("balance").GetDecimal().ShouldBe(0m);
        summary.GetProperty("payments").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Rejects_paying_more_than_the_balance()
    {
        await PayAsync(_token, 8000m, "Cash");

        var response = await PayAsync(_token, 501m, "Cash");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).ShouldBe("payment_exceeds_balance");
        (await SummaryAsync(_token)).GetProperty("paid").GetDecimal().ShouldBe(8000m);
    }

    [Fact]
    public async Task Simultaneous_payments_never_exceed_the_price()
    {
        // Each one alone would be valid; together they would collect the price several times.
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PayAsync(_token, 8500m, "Cash")));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(4);
        var summary = await SummaryAsync(_token);
        summary.GetProperty("paid").GetDecimal().ShouldBe(8500m);
        summary.GetProperty("balance").GetDecimal().ShouldBe(0m);
    }

    [Fact]
    public async Task Rejects_non_positive_amounts_extra_decimals_and_unknown_methods()
    {
        foreach (var amount in new[] { 0m, -5m, 10.005m })
        {
            var response = await PayAsync(_token, amount, "Cash");
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ErrorCodeAsync(response)).ShouldBe("validation_failed");
        }

        (await PayAsync(_token, 10m, "Bitcoin")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancelled_appointments_cannot_be_paid()
    {
        await SendAsync(HttpMethod.Put, $"/api/appointments/{_appointment}/status", _token, new { status = "Cancelled" });

        var response = await PayAsync(_token, 100m, "Cash");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).ShouldBe("appointment_not_payable");
    }

    [Fact]
    public async Task Unknown_appointment_is_not_found()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/payments", _token,
            new { appointmentId = Guid.CreateVersion7(), amount = 10m, method = "Cash" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(response)).ShouldBe("appointment_not_found");
    }

    [Fact]
    public async Task A_reversal_is_a_new_negative_movement_that_frees_the_balance_again()
    {
        var paid = await PayAsync(_token, 8500m, "Card");
        var payment = (await paid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("payments")[0]
            .GetProperty("id").GetGuid();

        var reversal = await ReverseAsync(_token, payment, "Cobro duplicado");

        reversal.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await reversal.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("paid").GetDecimal().ShouldBe(0m);
        body.GetProperty("balance").GetDecimal().ShouldBe(8500m);
        // Both rows are kept: the original and its inverse.
        var rows = body.GetProperty("payments").EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        rows[1].GetProperty("amount").GetDecimal().ShouldBe(-8500m);
        rows[1].GetProperty("reversesId").GetGuid().ShouldBe(payment);
        rows[1].GetProperty("reason").GetString().ShouldBe("Cobro duplicado");

        // The balance can be collected again.
        (await PayAsync(_token, 8500m, "Cash")).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_payment_can_be_reversed_only_once_and_a_reversal_cannot_be_reversed()
    {
        var paid = await PayAsync(_token, 1000m, "Cash");
        var payment = (await paid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("payments")[0]
            .GetProperty("id").GetGuid();
        var reversal = await ReverseAsync(_token, payment, "Error");
        var reversalId = (await reversal.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("payments")[1]
            .GetProperty("id").GetGuid();

        var again = await ReverseAsync(_token, payment, "Otra vez");
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(again)).ShouldBe("payment_already_reversed");

        var ofReversal = await ReverseAsync(_token, reversalId, "No");
        ofReversal.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(ofReversal)).ShouldBe("payment_not_reversible");
    }

    [Fact]
    public async Task Reversing_requires_a_reason_and_the_owner_role()
    {
        var paid = await PayAsync(_token, 1000m, "Cash");
        var payment = (await paid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("payments")[0]
            .GetProperty("id").GetGuid();

        (await ReverseAsync(_token, payment, "  ")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var staff = await TokenAsync($"staff-{_t.A.Slug}@test.local");
        (await ReverseAsync(staff, payment, "Error")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // ...but staff can collect.
        (await PayAsync(staff, 500m, "Cash")).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_daily_report_adds_up_by_method_and_nets_the_reversals()
    {
        await PayAsync(_token, 3000m, "Cash");
        await PayAsync(_token, 2000m, "Transfer");
        var card = await PayAsync(_token, 1500m, "Card");
        var cardId = (await card.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("payments")[2]
            .GetProperty("id").GetGuid();
        await ReverseAsync(_token, cardId, "Rechazada");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)).ToString("yyyy-MM-dd");
        var response = await SendAsync(HttpMethod.Get, $"/api/payments/daily?date={today}", _token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var day = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        day.GetProperty("total").GetDecimal().ShouldBe(5000m);
        day.GetProperty("items").GetArrayLength().ShouldBe(4);
        day.GetProperty("items")[0].GetProperty("customerName").GetString().ShouldBe("Ana Pérez");
        var byMethod = day.GetProperty("byMethod").EnumerateArray()
            .ToDictionary(m => m.GetProperty("method").GetString()!, m => m.GetProperty("total").GetDecimal());
        byMethod["Cash"].ShouldBe(3000m);
        byMethod["Transfer"].ShouldBe(2000m);
        byMethod["Card"].ShouldBe(0m);
    }

    [Fact]
    public async Task Other_tenant_cannot_see_pay_or_reverse()
    {
        var paid = await PayAsync(_token, 1000m, "Cash");
        var payment = (await paid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("payments")[0]
            .GetProperty("id").GetGuid();
        var tokenB = await TokenAsync(_t.B.OwnerEmail);

        (await SendAsync(HttpMethod.Get, $"/api/payments?appointmentId={_appointment}", tokenB)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await PayAsync(tokenB, 10m, "Cash")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReverseAsync(tokenB, payment, "Intento")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        (await SendAsync(HttpMethod.Get, $"/api/payments?appointmentId={_appointment}", null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Post, "/api/payments", null,
            new { appointmentId = _appointment, amount = 10m, method = "Cash" })).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    private Task<HttpResponseMessage> PayAsync(string token, decimal amount, string method) =>
        SendAsync(HttpMethod.Post, "/api/payments", token, new { appointmentId = _appointment, amount, method });

    private Task<HttpResponseMessage> ReverseAsync(string token, Guid paymentId, string reason) =>
        SendAsync(HttpMethod.Post, $"/api/payments/{paymentId}/reversal", token, new { reason });

    private async Task<JsonElement> SummaryAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/payments?appointmentId={_appointment}", token);
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
