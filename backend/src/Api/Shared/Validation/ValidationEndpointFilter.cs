using FluentValidation;

namespace Api.Shared.Validation;

/// <summary>
/// Endpoint filter factory applied to the root API group: for every endpoint argument whose
/// type has a registered <see cref="IValidator{T}"/>, the argument is validated before the
/// handler runs. Failures return a 400 ValidationProblemDetails. This makes validation
/// consistent across all endpoints without per-endpoint wiring.
/// </summary>
internal static class ValidationEndpointFilter
{
    public static EndpointFilterDelegate Create(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        var services = context.ApplicationServices;
        var validated = new List<(int Index, Type ValidatorType)>();
        var parameters = context.MethodInfo.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(parameters[i].ParameterType);
            using var scope = services.CreateScope();
            if (scope.ServiceProvider.GetService(validatorType) is not null)
            {
                validated.Add((i, validatorType));
            }
        }

        if (validated.Count == 0)
        {
            return next;
        }

        return async invocationContext =>
        {
            var errors = new Dictionary<string, string[]>();
            foreach (var (index, validatorType) in validated)
            {
                var argument = invocationContext.Arguments[index];
                if (argument is null)
                {
                    errors["body"] = ["Request body is required."];
                    continue;
                }

                var validator = (IValidator)invocationContext.HttpContext.RequestServices.GetRequiredService(validatorType);
                var result = await validator.ValidateAsync(
                    new ValidationContext<object>(argument),
                    invocationContext.HttpContext.RequestAborted);
                foreach (var group in result.Errors.GroupBy(e => ToCamelCase(e.PropertyName)))
                {
                    errors[group.Key] = [.. group.Select(e => e.ErrorCode)];
                }
            }

            if (errors.Count > 0)
            {
                return TypedResults.ValidationProblem(
                    errors,
                    title: "One or more validation errors occurred.",
                    extensions: new Dictionary<string, object?> { ["code"] = "validation_failed" });
            }

            return await next(invocationContext);
        };
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
