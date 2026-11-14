using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.Users.Domain;

/// <summary>
/// Opaque refresh token (only its SHA-256 hash is stored). Rotated on every use; all tokens
/// derived from the same login share a <see cref="FamilyId"/>, so reusing a rotated token
/// revokes the whole family. GLOBAL: it is looked up before the tenant is known.
/// </summary>
public sealed class RefreshToken : IGlobalEntity
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>Business the session is bound to (deliberately not named business_id: not tenant data).</summary>
    public Guid IssuedForBusinessId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;
    public Guid FamilyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    public static RefreshToken Create(Guid userId, Guid businessId, string tokenHash, Guid familyId,
        DateTimeOffset now, DateTimeOffset expiresAt) => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            IssuedForBusinessId = businessId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            CreatedAt = BusinessTimeZone.EnsureUtc(now),
            ExpiresAt = BusinessTimeZone.EnsureUtc(expiresAt),
        };
}
