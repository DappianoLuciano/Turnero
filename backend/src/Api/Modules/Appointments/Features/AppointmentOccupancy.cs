using Api.Modules.Appointments.Contracts;
using Api.Modules.Appointments.Domain;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Appointments.Features;

internal sealed class AppointmentOccupancy(AppDbContext db) : IAppointmentOccupancy
{
    public async Task<IReadOnlyList<OccupiedPeriod>> GetOccupiedAsync(
        Guid professionalId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await db.Set<Appointment>().AsNoTracking()
            .Where(a => a.ProfessionalId == professionalId
                && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Completed)
                && a.StartsAt < to && a.EndsAt > from)
            .OrderBy(a => a.StartsAt)
            .Select(a => new OccupiedPeriod(a.StartsAt, a.EndsAt))
            .ToListAsync(cancellationToken);
}
