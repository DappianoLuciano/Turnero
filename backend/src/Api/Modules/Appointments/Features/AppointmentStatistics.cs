using Api.Modules.Appointments.Contracts;
using Api.Modules.Appointments.Domain;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Appointments.Features;

internal sealed class AppointmentStatistics(AppDbContext db) : IAppointmentStatistics
{
    public async Task<AppointmentStats> GetAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var inRange = db.Set<Appointment>().AsNoTracking().Where(a => a.StartsAt >= from && a.StartsAt < to);

        var byStatus = await inRange.GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

        var completed = inRange.Where(a => a.Status == AppointmentStatus.Completed);
        var byProfessional = await completed.GroupBy(a => a.ProfessionalId)
            .Select(g => new GroupStats(g.Key, g.Count(), g.Sum(a => a.Price))).ToListAsync(cancellationToken);
        var byService = await completed.GroupBy(a => a.ServiceId)
            .Select(g => new GroupStats(g.Key, g.Count(), g.Sum(a => a.Price))).ToListAsync(cancellationToken);

        return new AppointmentStats(
            byStatus.OrderBy(s => s.Status).Select(s => new StatusCount(s.Status.ToString(), s.Count)).ToList(),
            byProfessional.Sum(p => p.Value),
            byProfessional.OrderByDescending(p => p.Value).ToList(),
            byService.OrderByDescending(s => s.Value).ToList());
    }
}
