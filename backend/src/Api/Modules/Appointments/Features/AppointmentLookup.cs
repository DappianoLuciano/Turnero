using Api.Modules.Appointments.Contracts;
using Api.Modules.Appointments.Domain;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Appointments.Features;

internal sealed class AppointmentLookup(AppDbContext db) : IAppointmentLookup
{
    public async Task<IReadOnlyList<AppointmentSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Set<Appointment>().AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new AppointmentSummary(
                a.Id, a.CustomerId, a.StartsAt, a.Price, a.Currency,
                a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Completed))
            .ToListAsync(cancellationToken);
    }

    public async Task LockAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
        {
            // Outside a transaction the lock would be released immediately (and RLS would see no tenant).
            throw new InvalidOperationException("Locking an appointment requires the request transaction.");
        }

        // RLS limits this to the current tenant's rows.
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM appointments WHERE id = {appointmentId} FOR UPDATE", cancellationToken);
    }

    public Task<int> CountUpcomingAsync(
        Guid customerId, DateTimeOffset from, CancellationToken cancellationToken = default) =>
        db.Set<Appointment>().AsNoTracking()
            .CountAsync(
                a => a.CustomerId == customerId && a.Status == AppointmentStatus.Scheduled && a.StartsAt >= from,
                cancellationToken);
}
