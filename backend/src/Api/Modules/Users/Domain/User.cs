using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.Users.Domain;

/// <summary>
/// A person who can log in. GLOBAL (no business_id): the same user may belong to several
/// businesses through <see cref="BusinessMembership"/>. Holds no business data (ADR 0005).
/// </summary>
public sealed class User : IGlobalEntity
{
    public const int EmailMaxLength = 254;
    public const int FullNameMaxLength = 120;

    private User()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Normalized (trimmed, lower-case). Unique.</summary>
    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static User Create(string email, string fullName, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        Email = NormalizeEmail(email),
        FullName = fullName.Trim(),
        IsActive = true,
        CreatedAt = BusinessTimeZone.EnsureUtc(now),
    };

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
