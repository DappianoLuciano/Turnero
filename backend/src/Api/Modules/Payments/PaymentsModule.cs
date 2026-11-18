using Api.Modules.Payments.Contracts;
using Api.Modules.Payments.Features;
using Api.Shared.Hosting;

namespace Api.Modules.Payments;

/// <summary>Payments (cobros) against appointments. Append-only: corrections are reversals (ADR 0009).</summary>
internal static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services) =>
        services.AddScoped<IPaymentLedger, PaymentLedger>();

    public static void MapPaymentsEndpoints(ApiRouteGroups groups)
    {
        ListAppointmentPayments.Map(groups.Tenant);
        ListDailyPayments.Map(groups.Tenant);
        CollectPayment.Map(groups.Tenant);
        ReversePayment.Map(groups.Tenant);
    }
}
