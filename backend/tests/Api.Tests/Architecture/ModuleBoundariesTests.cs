using System.Reflection;
using NetArchTest.Rules;

namespace Api.Tests.Architecture;

/// <summary>
/// Enforces the modular-monolith rules:
/// - a module only uses another module through its <c>Contracts</c> namespace;
/// - <c>Api.Shared</c> never depends on modules.
/// </summary>
public sealed class ModuleBoundariesTests
{
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;
    private const string ModulesNamespace = "Api.Modules";

    [Fact]
    public void Modules_only_depend_on_other_modules_through_contracts()
    {
        var violations = new List<string>();
        var modules = DiscoverModules().ToList();
        foreach (var module in modules)
        {
            var forbidden = modules
                .Where(other => other != module)
                .SelectMany(InternalNamespacesOf)
                .ToArray();
            if (forbidden.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(ApiAssembly)
                .That().ResideInNamespaceStartingWith($"{ModulesNamespace}.{module}.")
                .ShouldNot().HaveDependencyOnAny(forbidden)
                .GetResult();
            if (!result.IsSuccessful)
            {
                violations.Add($"{module}: {string.Join(", ", result.FailingTypeNames ?? [])}");
            }
        }

        violations.ShouldBeEmpty("Modules must use other modules only through their Contracts namespace.");
    }

    [Fact]
    public void Shared_does_not_depend_on_modules()
    {
        var result = Types.InAssembly(ApiAssembly)
            .That().ResideInNamespaceStartingWith("Api.Shared")
            .ShouldNot().HaveDependencyOn(ModulesNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static IEnumerable<string> DiscoverModules() =>
        ApiAssembly.GetTypes()
            .Select(t => t.Namespace)
            .OfType<string>()
            .Where(ns => ns.StartsWith($"{ModulesNamespace}.", StringComparison.Ordinal))
            .Select(ns => ns.Split('.')[2])
            .Distinct()
            .Order();

    /// <summary>Every namespace of a module except its public <c>Contracts</c>.</summary>
    private static IEnumerable<string> InternalNamespacesOf(string module)
    {
        var root = $"{ModulesNamespace}.{module}";
        return ApiAssembly.GetTypes()
            .Select(t => t.Namespace)
            .OfType<string>()
            .Where(ns => ns.StartsWith($"{root}.", StringComparison.Ordinal)
                && !ns.StartsWith($"{root}.Contracts", StringComparison.Ordinal))
            .Distinct();
    }
}
