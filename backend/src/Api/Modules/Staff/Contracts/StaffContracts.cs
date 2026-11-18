namespace Api.Modules.Staff.Contracts;

/// <summary>Public view of a professional for other modules.</summary>
public sealed record ProfessionalSummary(Guid Id, string Name, bool IsActive, IReadOnlyList<Guid> ServiceIds);

/// <summary>Read access to the professionals of the current tenant. Use this instead of querying <c>professionals</c>.</summary>
public interface IProfessionalDirectory
{
    /// <summary>The professionals of the current business that exist among <paramref name="ids"/> (unknown ids are omitted).</summary>
    Task<IReadOnlyList<ProfessionalSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
