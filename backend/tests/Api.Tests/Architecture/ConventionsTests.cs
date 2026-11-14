using System.Reflection;
using FluentValidation;

namespace Api.Tests.Architecture;

public sealed class ConventionsTests
{
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Every_request_type_has_a_validator()
    {
        var validated = ApiAssembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true })
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();

        var missing = ApiAssembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Api.Modules", StringComparison.Ordinal) == true)
            .Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal))
            .Where(t => !validated.Contains(t))
            .Select(t => t.FullName)
            .ToList();

        missing.ShouldBeEmpty("Every *Request type needs an AbstractValidator<T> in its feature.");
    }

    [Fact]
    public void No_float_or_double_properties_in_entities()
    {
        var offenders = ApiAssembly.GetTypes()
            .Where(t => t.Namespace?.Contains(".Domain", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetProperties().Select(p => (Type: t, Property: p)))
            .Where(x => x.Property.PropertyType == typeof(float) || x.Property.PropertyType == typeof(double)
                || Nullable.GetUnderlyingType(x.Property.PropertyType) is { } u && (u == typeof(float) || u == typeof(double)))
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .ToList();

        offenders.ShouldBeEmpty("Money and quantities must use decimal (numeric), never float/double.");
    }

    [Fact]
    public void No_DateTime_properties_in_entities()
    {
        var offenders = ApiAssembly.GetTypes()
            .Where(t => t.Namespace?.Contains(".Domain", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetProperties().Select(p => (Type: t, Property: p)))
            .Where(x => x.Property.PropertyType == typeof(DateTime) || x.Property.PropertyType == typeof(DateTime?))
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .ToList();

        offenders.ShouldBeEmpty("Instants are DateTimeOffset in UTC (timestamptz).");
    }
}
