using System.Text.RegularExpressions;
using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.Businesses.Domain;

/// <summary>The tenant. Its <see cref="Id"/> is the <c>business_id</c> of every business table.</summary>
public sealed partial class Business : ITenantRoot
{
    public const int NameMaxLength = 120;
    public const int SlugMaxLength = 63;

    private Business()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Public identifier for the booking site (/r/{slug} or {slug}.domain).</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>IANA time zone used for every local-time conversion of this business.</summary>
    public string TimeZone { get; private set; } = BusinessTimeZone.Default;

    public string Currency { get; private set; } = Money.DefaultCurrency;
    public DateTimeOffset CreatedAt { get; private set; }

    public static Business Create(string name, string slug, DateTimeOffset now, string? timeZone = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!IsValidSlug(slug))
        {
            throw new ArgumentException("Invalid slug.", nameof(slug));
        }

        var zone = timeZone ?? BusinessTimeZone.Default;
        if (!BusinessTimeZone.IsValid(zone))
        {
            throw new ArgumentException("Unknown time zone.", nameof(timeZone));
        }

        return new Business
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Slug = slug,
            TimeZone = zone,
            Currency = Money.DefaultCurrency,
            CreatedAt = BusinessTimeZone.EnsureUtc(now),
        };
    }

    /// <summary>Lowercase letters, digits and inner hyphens; valid as a DNS label (subdomain-ready).</summary>
    public static bool IsValidSlug(string? slug) =>
        slug is { Length: > 0 and <= SlugMaxLength } && SlugRegex().IsMatch(slug);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugRegex();
}
