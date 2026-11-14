namespace Api.Shared.Auth;

/// <summary>Claim names used in access tokens (kept short and unmapped).</summary>
public static class AppClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string BusinessId = "business_id";
    public const string Role = "role";
}

/// <summary>Role of a user within a business (per membership, not global).</summary>
public enum BusinessRole
{
    Owner,
    Staff,
    Reception,
}

public static class AuthPolicies
{
    /// <summary>Any authenticated member of the current business.</summary>
    public const string Member = "member";

    public const string Owner = "owner";

    /// <summary>Front desk operations (agenda, customers, cash).</summary>
    public const string OwnerOrReception = "owner_or_reception";
}
