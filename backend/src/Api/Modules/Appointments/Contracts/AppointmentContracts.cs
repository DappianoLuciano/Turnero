namespace Api.Modules.Appointments.Contracts;

/// <summary>Public view of an appointment for other modules (for example, to collect payments).</summary>
/// <param name="AcceptsPayments">False for cancelled and no-show appointments.</param>
public sealed record AppointmentSummary(
    Guid Id, Guid CustomerId, DateTimeOffset StartsAt, decimal Price, string Currency, bool AcceptsPayments);

/// <summary>Read access to the appointments of the current tenant. Use this instead of querying <c>appointments</c>.</summary>
public interface IAppointmentLookup
{
    /// <summary>The appointments of the current business that exist among <paramref name="ids"/> (unknown ids are omitted).</summary>
    Task<IReadOnlyList<AppointmentSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

/// <summary>Number of appointments in one status.</summary>
public sealed record StatusCount(string Status, int Count);

/// <summary>Completed appointments of one professional or service: how many and how much they were worth.</summary>
public sealed record GroupStats(Guid Id, int Count, decimal Value);

public sealed record AppointmentStats(
    IReadOnlyList<StatusCount> ByStatus,
    decimal CompletedValue,
    IReadOnlyList<GroupStats> ByProfessional,
    IReadOnlyList<GroupStats> ByService);

/// <summary>Aggregates over the appointments of the current tenant (for reports).</summary>
public interface IAppointmentStatistics
{
    /// <summary>Appointments starting in [<paramref name="from"/>, <paramref name="to"/>).</summary>
    Task<AppointmentStats> GetAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A period of time already taken by an appointment.</summary>
public sealed record OccupiedPeriod(DateTimeOffset Start, DateTimeOffset End);

/// <summary>Which periods of a professional are taken (for availability).</summary>
public interface IAppointmentOccupancy
{
    /// <summary>Periods of scheduled or completed appointments of the professional that intersect [from, to).</summary>
    Task<IReadOnlyList<OccupiedPeriod>> GetOccupiedAsync(
        Guid professionalId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
