namespace Api.Shared.Hosting;

/// <summary>
/// Minimal <c>.env</c> loader for local development. Walks up from the current directory,
/// loads the first <c>.env</c> found and sets only variables that are not already defined,
/// so real environment variables (CI, production) always win. Never used outside Development.
/// </summary>
public static class DotEnv
{
    public static void LoadForDevelopment()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var file = FindUpwards(Directory.GetCurrentDirectory(), ".env");
        if (file is null)
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(file))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? FindUpwards(string start, string fileName)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
