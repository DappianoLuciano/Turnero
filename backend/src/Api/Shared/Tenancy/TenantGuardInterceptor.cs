using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Api.Shared.Tenancy;

/// <summary>
/// First isolation layer for writes (EF level). Stamps <c>BusinessId</c> on new tenant entities
/// and refuses any insert/update/delete of an entity that belongs to another tenant.
/// RLS rejects these too; failing here gives a clear error before reaching the database.
/// </summary>
public sealed class TenantGuardInterceptor(ITenantContext tenant) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Guard(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            Guid? owner = entry.Entity switch
            {
                ITenantEntity e => e.BusinessId,
                ITenantRoot r => r.Id,
                _ => null,
            };
            if (owner is null)
            {
                continue;
            }

            if (entry.State == EntityState.Added && owner == Guid.Empty && entry.Entity is ITenantEntity)
            {
                entry.Property(nameof(ITenantEntity.BusinessId)).CurrentValue = tenant.RequireBusinessId();
                continue;
            }

            if (owner != tenant.BusinessId)
            {
                throw new CrossTenantAccessException(entry.Metadata.ClrType.Name, entry.State.ToString());
            }
        }
    }
}

public sealed class CrossTenantAccessException(string entity, string operation)
    : InvalidOperationException($"Cross-tenant {operation} of '{entity}' rejected.");
