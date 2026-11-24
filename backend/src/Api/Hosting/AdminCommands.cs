using Api.Modules.Businesses.Contracts;
using Api.Modules.Subscriptions.Contracts;
using Api.Shared.Errors;

namespace Api.Hosting;

/// <summary>
/// Operator command to register a subscription payment (there is no payment gateway yet, ADR 0014):
/// <c>dotnet run --project src/Api -- activate &lt;slug&gt; [days=30]</c>. Works in any environment. Uses only contracts.
/// </summary>
internal static class AdminCommands
{
    public const string Activate = "activate";
    public const int DefaultDays = 30;

    public static async Task<int> RunActivateAsync(WebApplication app, string[] args)
    {
        var index = Array.IndexOf(args, Activate);
        var slug = args.ElementAtOrDefault(index + 1);
        var daysText = args.ElementAtOrDefault(index + 2);
        var days = DefaultDays;
        if (string.IsNullOrWhiteSpace(slug) || (daysText is not null && !int.TryParse(daysText, out days)))
        {
            Console.Error.WriteLine("Usage: activate <slug> [days]");
            return 2;
        }

        await using var lookupScope = app.Services.CreateAsyncScope();
        var businessId = await lookupScope.ServiceProvider.GetRequiredService<IBusinessLookup>().FindIdBySlugAsync(slug);
        if (businessId is null)
        {
            Console.Error.WriteLine($"No business with slug '{slug}'.");
            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        try
        {
            var summary = await scope.ServiceProvider.GetRequiredService<ISubscriptionAdmin>()
                .ActivateAsync(businessId.Value, days);
            Console.WriteLine($"'{slug}' active until {summary.PaidUntil:u} ({summary.DaysLeft} days left).");
            return 0;
        }
        catch (Exception ex) when (ex is AppException or ArgumentOutOfRangeException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
