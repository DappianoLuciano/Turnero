namespace Api.Shared.Time;

/// <summary>
/// The ONLY place where UTC instants are converted to/from a business's local time.
/// Persisted values are always UTC (<c>timestamptz</c>); local values exist only at the edges
/// (UI, schedules expressed in local wall-clock time).
/// </summary>
public static class BusinessTimeZone
{
    public const string Default = "America/Argentina/Buenos_Aires";

    public static TimeZoneInfo Resolve(string ianaId) => TimeZoneInfo.FindSystemTimeZoneById(ianaId);

    public static bool IsValid(string ianaId) => TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out _);

    /// <summary>Converts a UTC instant to the business's local wall-clock time.</summary>
    public static DateTimeOffset ToLocal(DateTimeOffset utc, string ianaId) =>
        TimeZoneInfo.ConvertTime(utc, Resolve(ianaId));

    /// <summary>
    /// Converts a local wall-clock time in the business's zone to a UTC instant.
    /// Invalid local times (skipped by a DST jump) are moved forward by the gap;
    /// ambiguous ones (repeated) resolve to the earlier offset.
    /// </summary>
    public static DateTimeOffset ToUtc(DateTime localWallClock, string ianaId)
    {
        var zone = Resolve(ianaId);
        var local = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            var adjustment = zone.GetAdjustmentRules()
                .FirstOrDefault(r => r.DateStart <= local && r.DateEnd >= local);
            local = local.Add(adjustment?.DaylightDelta ?? TimeSpan.FromHours(1));
        }

        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>The UTC range [from, to) covering the local calendar days <paramref name="first"/>..<paramref name="last"/> (inclusive).</summary>
    public static (DateTimeOffset From, DateTimeOffset To) LocalDaysToUtc(DateOnly first, DateOnly last, string ianaId) =>
        (ToUtc(first.ToDateTime(TimeOnly.MinValue), ianaId), ToUtc(last.AddDays(1).ToDateTime(TimeOnly.MinValue), ianaId));

    /// <summary>Normalizes any DateTimeOffset to UTC (offset 0), as required by Npgsql for timestamptz.</summary>
    public static DateTimeOffset EnsureUtc(DateTimeOffset value) => value.ToUniversalTime();
}
