using Api.Modules.Appointments.Contracts;
using Api.Modules.Businesses.Contracts;
using Api.Modules.Payments.Contracts;
using Api.Modules.Services.Contracts;
using Api.Modules.Staff.Contracts;
using Api.Shared.Auth;
using Api.Shared.Errors;
using Api.Shared.Time;

namespace Api.Modules.Reports.Features;

public sealed record ReportMethodTotal(string Method, decimal Total);

public sealed record ReportStatusCount(string Status, int Count);

/// <summary>Completed appointments of one professional or service.</summary>
public sealed record ReportGroup(Guid Id, string Name, int Count, decimal Value);

/// <summary>Business summary of a range of local days (both ends included).</summary>
/// <param name="Collected">Net money collected (collections minus reversals) registered in the range.</param>
/// <param name="CompletedValue">Sum of the prices of the appointments completed in the range.</param>
public sealed record SummaryReportResponse(
    DateOnly From,
    DateOnly To,
    string Currency,
    decimal Collected,
    IReadOnlyList<ReportMethodTotal> ByMethod,
    IReadOnlyList<ReportStatusCount> AppointmentsByStatus,
    decimal CompletedValue,
    IReadOnlyList<ReportGroup> ByProfessional,
    IReadOnlyList<ReportGroup> ByService);

/// <summary>Builds the summary of a range of local days from the other modules' public contracts.</summary>
internal sealed class SummaryReportBuilder(
    IBusinessDirectory businesses, IPaymentLedger ledger, IAppointmentStatistics appointments,
    IProfessionalDirectory professionals, IServiceCatalog catalog)
{
    public const int MaxDays = 366;

    public async Task<SummaryReportResponse> BuildAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "invalid_range",
                $"The range must be valid and span at most {MaxDays} days.");
        }

        var business = await businesses.GetCurrentAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");
        var (start, end) = BusinessTimeZone.LocalDaysToUtc(from, to, business.TimeZone);

        var methods = await ledger.GetTotalsByMethodAsync(start, end, cancellationToken);
        var stats = await appointments.GetAsync(start, end, cancellationToken);

        var professionalNames = (await professionals.GetByIdsAsync(
                stats.ByProfessional.Select(p => p.Id).ToList(), cancellationToken))
            .ToDictionary(p => p.Id, p => p.Name);
        var serviceNames = (await catalog.GetByIdsAsync(
                stats.ByService.Select(s => s.Id).ToList(), cancellationToken))
            .ToDictionary(s => s.Id, s => s.Name);

        return new SummaryReportResponse(
            from, to, business.Currency,
            methods.Sum(m => m.Total),
            methods.Select(m => new ReportMethodTotal(m.Method, m.Total)).ToList(),
            stats.ByStatus.Select(s => new ReportStatusCount(s.Status, s.Count)).ToList(),
            stats.CompletedValue,
            stats.ByProfessional.Select(p =>
                new ReportGroup(p.Id, professionalNames.GetValueOrDefault(p.Id, string.Empty), p.Count, p.Value)).ToList(),
            stats.ByService.Select(s =>
                new ReportGroup(s.Id, serviceNames.GetValueOrDefault(s.Id, string.Empty), s.Count, s.Value)).ToList());
    }
}

/// <summary>GET /api/reports/summary?from=YYYY-MM-DD&amp;to=YYYY-MM-DD (owner only).</summary>
internal static class GetSummaryReport
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/reports/summary", HandleAsync)
            .WithName("GetSummaryReport")
            .WithTags("Reports")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces<SummaryReportResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> HandleAsync(
        DateOnly from, DateOnly to, SummaryReportBuilder builder, CancellationToken cancellationToken) =>
        TypedResults.Ok(await builder.BuildAsync(from, to, cancellationToken));
}

/// <summary>GET /api/reports/summary/export?from=&amp;to=: the same summary as an Excel workbook (owner only).</summary>
internal static class ExportSummaryReport
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/reports/summary/export", HandleAsync)
            .WithName("ExportSummaryReport")
            .WithTags("Reports")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces(StatusCodes.Status200OK, contentType: ContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> HandleAsync(
        DateOnly from, DateOnly to, SummaryReportBuilder builder, CancellationToken cancellationToken)
    {
        var report = await builder.BuildAsync(from, to, cancellationToken);
        return TypedResults.File(ReportWorkbook.Build(report), ContentType,
            $"reporte-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.xlsx");
    }
}
