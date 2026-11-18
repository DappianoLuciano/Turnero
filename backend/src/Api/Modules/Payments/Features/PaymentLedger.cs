using Api.Modules.Payments.Contracts;
using Api.Modules.Payments.Domain;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Payments.Features;

internal sealed class PaymentLedger(AppDbContext db) : IPaymentLedger
{
    public async Task<IReadOnlyList<PaymentMethodTotal>> GetTotalsByMethodAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var rows = await db.Set<Payment>().AsNoTracking()
            .Where(p => p.CreatedAt >= from && p.CreatedAt < to)
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Total = g.Sum(p => p.Amount) })
            .ToListAsync(cancellationToken);
        return rows.OrderBy(r => r.Method).Select(r => new PaymentMethodTotal(r.Method.ToString(), r.Total)).ToList();
    }
}
