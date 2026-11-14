using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Shared.Tenancy;

/// <summary>
/// Runs work inside a transaction bound to an explicit tenant. For flows that do not come from
/// an authenticated panel request: login/refresh (tenant discovered mid-request), provisioning
/// a new business, dev seed. Regular endpoints rely on <see cref="TenantTransactionFilter"/>.
/// </summary>
public sealed class TenantScope(AppDbContext db, TenantContext tenant)
{
    public async Task<T> RunAsync<T>(Guid? businessId, Guid? userId, Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("A transaction is already open; tenant settings would not be applied.");
        }

        tenant.Set(businessId, userId);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task RunAsync(Guid? businessId, Guid? userId, Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default) =>
        RunAsync<bool>(businessId, userId, async ct =>
        {
            await work(ct);
            return true;
        }, cancellationToken);
}
