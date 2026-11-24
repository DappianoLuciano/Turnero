using Api.Hosting;
using Api.Shared.Hosting;

DotEnv.LoadForDevelopment();

var builder = WebApplication.CreateBuilder(args);
builder.AddApi();

var app = builder.Build();
await app.UseApiAsync();

if (args.Contains(DevSeed.Command))
{
    await DevSeed.RunAsync(app);
    return 0;
}

if (args.Contains(AdminCommands.Activate))
{
    return await AdminCommands.RunActivateAsync(app, args);
}

await app.RunAsync();
return 0;

/// <summary>Entry point marker, used by WebApplicationFactory in integration tests.</summary>
public partial class Program;
