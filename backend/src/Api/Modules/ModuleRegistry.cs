using Api.Shared.Hosting;

namespace Api.Modules;

/// <summary>
/// Registers every module. To add a module: create <c>Modules/&lt;Name&gt;/&lt;Name&gt;Module.cs</c>
/// with <c>Add&lt;Name&gt;Module</c> / <c>Map&lt;Name&gt;Endpoints</c> and call them here.
/// </summary>
internal static class ModuleRegistry
{
    public static IServiceCollection AddModules(this IServiceCollection services)
    {
        return services;
    }

    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder app, ApiRouteGroups groups)
    {
        _ = groups;
        return app;
    }
}
