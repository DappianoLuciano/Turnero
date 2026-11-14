using Api.Hosting;
using Api.Shared.Hosting;

DotEnv.LoadForDevelopment();

var builder = WebApplication.CreateBuilder(args);
builder.AddApi();

var app = builder.Build();
app.UseApi();

await app.RunAsync();

/// <summary>Entry point marker, used by WebApplicationFactory in integration tests.</summary>
public partial class Program;
