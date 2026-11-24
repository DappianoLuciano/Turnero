using Api.Shared.Auth;

namespace Api.Modules.Users.Contracts;

/// <summary>Creates users and memberships (signup, invitations, dev seed).</summary>
public interface IUserProvisioning
{
    /// <summary>
    /// Ensures a user with <paramref name="email"/> exists (creating it with the given password if not)
    /// and adds a membership to <paramref name="businessId"/>. Runs in its own tenant transaction.
    /// </summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a NEW user as owner of <paramref name="businessId"/>. Unlike <see cref="AddMemberAsync"/> it never reuses
    /// an existing user: it throws a 409 <c>email_taken</c> instead, so nobody can attach themselves to someone else's
    /// account by signing up with their email.
    /// </summary>
    Task<Guid> RegisterOwnerAsync(Guid businessId, string email, string fullName, string password,
        CancellationToken cancellationToken = default);

    Task<Guid> AddMemberAsync(Guid businessId, string email, string fullName, string password, BusinessRole role,
        CancellationToken cancellationToken = default);
}
