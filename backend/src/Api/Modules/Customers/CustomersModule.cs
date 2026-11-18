using Api.Modules.Customers.Contracts;
using Api.Modules.Customers.Features;
using Api.Shared.Hosting;

namespace Api.Modules.Customers;

/// <summary>Customers of the business: contact data and notes.</summary>
internal static class CustomersModule
{
    public static IServiceCollection AddCustomersModule(this IServiceCollection services) =>
        services.AddScoped<ICustomerDirectory, CustomerDirectory>()
            .AddScoped<ICustomerRegistry, CustomerRegistry>();

    public static void MapCustomersEndpoints(ApiRouteGroups groups)
    {
        ListCustomers.Map(groups.Tenant);
        GetCustomer.Map(groups.Tenant);
        CreateCustomer.Map(groups.Tenant);
        UpdateCustomer.Map(groups.Tenant);
        DeleteCustomer.Map(groups.Tenant);
    }
}
