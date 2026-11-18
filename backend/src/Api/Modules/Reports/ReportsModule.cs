using Api.Modules.Reports.Features;
using Api.Shared.Hosting;

namespace Api.Modules.Reports;

/// <summary>Reports: read-only summaries built from the other modules' public contracts. Owns no tables.</summary>
internal static class ReportsModule
{
    public static IServiceCollection AddReportsModule(this IServiceCollection services) =>
        services.AddScoped<SummaryReportBuilder>();

    public static void MapReportsEndpoints(ApiRouteGroups groups)
    {
        GetSummaryReport.Map(groups.Tenant);
        ExportSummaryReport.Map(groups.Tenant);
    }
}
