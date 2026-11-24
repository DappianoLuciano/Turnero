using Api.Modules.Appointments;
using Api.Modules.Booking;
using Api.Modules.Businesses;
using Api.Modules.CashRegister;
using Api.Modules.Customers;
using Api.Modules.Payments;
using Api.Modules.Reports;
using Api.Modules.Schedules;
using Api.Modules.Services;
using Api.Modules.Signup;
using Api.Modules.Staff;
using Api.Modules.Subscriptions;
using Api.Modules.Users;
using Api.Shared.Hosting;

namespace Api.Modules;

/// <summary>
/// Registers every module. To add a module: create <c>Modules/&lt;Name&gt;/&lt;Name&gt;Module.cs</c>
/// with <c>Add&lt;Name&gt;Module</c> / <c>Map&lt;Name&gt;Endpoints</c> and call them here.
/// </summary>
internal static class ModuleRegistry
{
    public static IServiceCollection AddModules(this IServiceCollection services) =>
        services
            .AddAppointmentsModule()
            .AddBookingModule()
            .AddBusinessesModule()
            .AddCashRegisterModule()
            .AddCustomersModule()
            .AddPaymentsModule()
            .AddReportsModule()
            .AddSchedulesModule()
            .AddServicesModule()
            .AddSignupModule()
            .AddStaffModule()
            .AddSubscriptionsModule()
            .AddUsersModule();

    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder app, ApiRouteGroups groups)
    {
        AppointmentsModule.MapAppointmentsEndpoints(groups);
        BookingModule.MapBookingEndpoints(groups);
        BusinessesModule.MapBusinessesEndpoints(groups);
        CashRegisterModule.MapCashRegisterEndpoints(groups);
        CustomersModule.MapCustomersEndpoints(groups);
        PaymentsModule.MapPaymentsEndpoints(groups);
        ReportsModule.MapReportsEndpoints(groups);
        SchedulesModule.MapSchedulesEndpoints(groups);
        ServicesModule.MapServicesEndpoints(groups);
        SignupModule.MapSignupEndpoints(groups);
        StaffModule.MapStaffEndpoints(groups);
        SubscriptionsModule.MapSubscriptionsEndpoints(groups);
        UsersModule.MapUsersEndpoints(groups);
        return app;
    }
}
