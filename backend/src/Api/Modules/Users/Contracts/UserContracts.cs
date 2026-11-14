using Api.Shared.Auth;

namespace Api.Modules.Users.Contracts;

/// <summary>Creates users and memberships (signup, invitations, dev seed).</summary>
public interface IUserProvisioning
{
    /// <summary>
    /// Ensures a user with <paramref name="email"/> exists (creating it with the given password if not)
    /// and adds a membership to <paramref name="businessId"/>. Runs in its own tenant transaction.
    /// </summary>
    Task<Guid> AddMemberAsync(Guid businessId, string email, string fullName, string password, BusinessRole role,
        CancellationToken cancellationToken = default);
}
