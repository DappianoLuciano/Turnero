using Api.Modules.CashRegister.Features;
using Api.Shared.Hosting;

namespace Api.Modules.CashRegister;

/// <summary>Cash register (caja): cash of the day = cash payments + manual movements, with a daily count.</summary>
internal static class CashRegisterModule
{
    public static IServiceCollection AddCashRegisterModule(this IServiceCollection services) =>
        services.AddScoped<CashDays>();

    public static void MapCashRegisterEndpoints(ApiRouteGroups groups)
    {
        GetCashDay.Map(groups.Tenant);
        RegisterCashMovement.Map(groups.Tenant);
        ReverseCashMovement.Map(groups.Tenant);
        CloseCashDay.Map(groups.Tenant);
    }
}
