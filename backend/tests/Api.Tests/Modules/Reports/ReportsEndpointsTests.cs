using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.Infrastructure;

namespace Api.Tests.Modules.Reports;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReportsEndpointsTests(PostgresFixture db) : IAsyncLifetime
{
    // Buenos Aires is UTC-3 all year (no DST): 10:00 local is 13:00Z.
    private static readonly DateTimeOffset Nov18 = new(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(-3));

    private TwoTenants _t = null!;
    private HttpClient _client = null!;
    private string _token = null!;
    private Guid _customer;
    private Guid _cut;
    private Guid _beard;
    private Guid _lucas;
    private Guid _mateo;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _t = await TwoTenants.CreateAsync(db);
        _client = db.Api.CreateApiClient();
        _token = await TokenAsync(_t.A.OwnerEmail);
        _customer = await CreateAsync("/api/customers", new { name = "Ana Pérez" });
        _cut = await CreateAsync("/api/services", new { name = "Corte", durationMinutes = 30, price = 8000m });
        _beard = await CreateAsync("/api/services", new { name = "Barba", durationMinutes = 20, price = 3000m });
        _lucas = await CreateAsync("/api/professionals", new { name = "Lucas", serviceIds = new[] { _cut, _beard } });
        _mateo = await CreateAsync("/api/professionals", new { name = "Mateo", serviceIds = new[] { _cut } });
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Counts_appointments_by_status_and_values_only_the_completed_ones_by_professional_and_service()
    {
        await BookAsync(_lucas, _cut, Nov18, "Completed");
        await BookAsync(_lucas, _beard, Nov18.AddHours(1), "Completed");
        await BookAsync(_mateo, _cut, Nov18, "Completed");
        await BookAsync(_mateo, _cut, Nov18.AddHours(2), "Cancelled");
        await BookAsync(_lucas, _cut, Nov18.AddHours(3), null);
        // Outside the range.
        await BookAsync(_lucas, _cut, Nov18.AddDays(40), "Completed");

        var report = await ReportAsync(_token, "2026-11-01", "2026-11-30");

        report.GetProperty("completedValue").GetDecimal().ShouldBe(19000m);
        var statuses = report.GetProperty("appointmentsByStatus").EnumerateArray()
            .ToDictionary(s => s.GetProperty("status").GetString()!, s => s.GetProperty("count").GetInt32());
        statuses.ShouldBe(new Dictionary<string, int> { ["Scheduled"] = 1, ["Completed"] = 3, ["Cancelled"] = 1 }, ignoreOrder: true);

        var byProfessional = report.GetProperty("byProfessional").EnumerateArray().ToList();
        byProfessional.Select(p => p.GetProperty("name").GetString()).ShouldBe(["Lucas", "Mateo"]);
        byProfessional[0].GetProperty("count").GetInt32().ShouldBe(2);
        byProfessional[0].GetProperty("value").GetDecimal().ShouldBe(11000m);
        byProfessional[1].GetProperty("value").GetDecimal().ShouldBe(8000m);

        var byService = report.GetProperty("byService").EnumerateArray().ToList();
        byService.Select(s => s.GetProperty("name").GetString()).ShouldBe(["Corte", "Barba"]);
        byService[0].GetProperty("count").GetInt32().ShouldBe(2);
        byService[0].GetProperty("value").GetDecimal().ShouldBe(16000m);
    }

    [Fact]
    public async Task Collected_money_is_the_net_of_collections_and_reversals_by_method()
    {
        var appointment = await BookAsync(_lucas, _cut, Nov18.AddDays(1), null);
        await PayAsync(appointment, 3000m, "Cash");
        await PayAsync(appointment, 2000m, "Transfer");
        var card = await PayAsync(appointment, 1000m, "Card");
        (await SendAsync(HttpMethod.Post, $"/api/payments/{card}/reversal", _token, new { reason = "Rechazada" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)).ToString("yyyy-MM-dd");
        var report = await ReportAsync(_token, today, today);

        report.GetProperty("collected").GetDecimal().ShouldBe(5000m);
        var methods = report.GetProperty("byMethod").EnumerateArray()
            .ToDictionary(m => m.GetProperty("method").GetString()!, m => m.GetProperty("total").GetDecimal());
        methods["Cash"].ShouldBe(3000m);
        methods["Transfer"].ShouldBe(2000m);
        methods["Card"].ShouldBe(0m);
    }

    [Fact]
    public async Task A_range_without_data_returns_empty_lists()
    {
        var report = await ReportAsync(_token, "2020-01-01", "2020-01-31");

        report.GetProperty("collected").GetDecimal().ShouldBe(0m);
        report.GetProperty("appointmentsByStatus").GetArrayLength().ShouldBe(0);
        report.GetProperty("byProfessional").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Rejects_inverted_or_too_long_ranges()
    {
        (await SendAsync(HttpMethod.Get, "/api/reports/summary?from=2026-11-30&to=2026-11-01", _token))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Get, "/api/reports/summary?from=2024-01-01&to=2026-01-01", _token))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Is_owner_only_and_isolated_between_businesses()
    {
        await BookAsync(_lucas, _cut, Nov18, "Completed");

        var staff = await TokenAsync($"staff-{_t.A.Slug}@test.local");
        (await SendAsync(HttpMethod.Get, "/api/reports/summary?from=2026-11-01&to=2026-11-30", staff))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/reports/summary?from=2026-11-01&to=2026-11-30", null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var other = await ReportAsync(await TokenAsync(_t.B.OwnerEmail), "2026-11-01", "2026-11-30");
        other.GetProperty("completedValue").GetDecimal().ShouldBe(0m);
        other.GetProperty("byProfessional").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Exports_the_summary_as_an_excel_workbook_with_numeric_amounts()
    {
        await BookAsync(_lucas, _cut, Nov18, "Completed");
        await BookAsync(_mateo, _cut, Nov18, "Completed");

        var response = await SendAsync(HttpMethod.Get, "/api/reports/summary/export?from=2026-11-01&to=2026-11-30", _token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType
            .ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.FileName.ShouldBe("reporte-2026-11-01-2026-11-30.xlsx");

        await using var stream = await response.Content.ReadAsStreamAsync(Ct);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        workbook.Worksheets.Select(w => w.Name)
            .ShouldBe(["Resumen", "Cobrado por medio", "Turnos por estado", "Por profesional", "Por servicio"]);
        var summary = workbook.Worksheet("Resumen");
        summary.Cell(2, 2).GetString().ShouldBe("01/11/2026");
        summary.Cell(3, 2).GetString().ShouldBe("30/11/2026");
        summary.Cell(6, 2).GetDouble().ShouldBe(16000d);
        var byService = workbook.Worksheet("Por servicio");
        byService.Cell(2, 1).GetString().ShouldBe("Corte");
        byService.Cell(2, 2).GetDouble().ShouldBe(2d);
        byService.Cell(2, 3).GetDouble().ShouldBe(16000d);
    }

    [Fact]
    public async Task The_export_is_owner_only_and_validates_the_range()
    {
        var staff = await TokenAsync($"staff-{_t.A.Slug}@test.local");
        (await SendAsync(HttpMethod.Get, "/api/reports/summary/export?from=2026-11-01&to=2026-11-30", staff))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/reports/summary/export?from=2026-11-30&to=2026-11-01", _token))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<JsonElement> ReportAsync(string token, string from, string to)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/reports/summary?from={from}&to={to}", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    /// <summary>Books an appointment and moves it to <paramref name="status"/> (null leaves it scheduled).</summary>
    private async Task<Guid> BookAsync(Guid professional, Guid service, DateTimeOffset startsAt, string? status)
    {
        var id = await CreateAsync("/api/appointments", new
        {
            customerId = _customer,
            professionalId = professional,
            serviceId = service,
            startsAt,
            notes = (string?)null,
        });
        if (status is not null)
        {
            (await SendAsync(HttpMethod.Put, $"/api/appointments/{id}/status", _token, new { status }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        return id;
    }

    private async Task<Guid> PayAsync(Guid appointment, decimal amount, string method)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/payments", _token,
            new { appointmentId = appointment, amount, method });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var payments = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("payments");
        return payments[payments.GetArrayLength() - 1].GetProperty("id").GetGuid();
    }

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
