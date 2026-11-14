using System.Reflection;

namespace Api.Shared.Hosting;

public static class BuildTooling
{
    /// <summary>
    /// True when the app is started by the build-time OpenAPI generator (GetDocument).
    /// In that mode there is no database nor secrets, so startup validation and migrations are skipped.
    /// </summary>
    public static bool IsOpenApiGeneration { get; } =
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
