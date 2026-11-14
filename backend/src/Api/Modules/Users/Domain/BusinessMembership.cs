using Api.Shared.Auth;
using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.Users.Domain;

/// <summary>A user's role inside one business. Tenant data: RLS by business_id.</summary>
public sealed class BusinessMembership : ITenantEntity
{
    private BusinessMembership()
    {
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid UserId { get; private set; }
    public BusinessRole Role { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static BusinessMembership Create(Guid businessId, Guid userId, BusinessRole role, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        BusinessId = businessId,
        UserId = userId,
        Role = role,
        CreatedAt = BusinessTimeZone.EnsureUtc(now),
    };

    public void ChangeRole(BusinessRole role) => Role = role;
}
