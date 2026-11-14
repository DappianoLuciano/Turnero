using FluentValidation;

namespace Api.Shared.Validation;

internal static class ValidationServiceCollectionExtensions
{
    public static IServiceCollection AddRequestValidation(this IServiceCollection services)
    {
        // Validators live next to their feature (vertical slice) and are discovered by convention.
        services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton, includeInternalTypes: true);
        ValidatorOptions.Global.PropertyNameResolver = (_, member, _) => member?.Name;
        return services;
    }
}
