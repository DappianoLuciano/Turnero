using System.Security.Cryptography;
using System.Text;
using Api.Modules.Appointments.Contracts;
using Api.Modules.Appointments.Domain;
using Api.Shared.Auth;
using Api.Shared.Errors;
using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.Appointments.Features;

/// <summary>
/// Token = appointment id (32 hex) + first 16 bytes of HMAC-SHA256(business id, appointment id) (32 hex), keyed with a
/// key derived from the JWT signing key (domain separated). Only the server can issue it; a token of one business never
/// validates in another because the business id is part of the signed data.
/// </summary>
internal sealed class AppointmentLinks(AppDbContext db, ITenantContext tenant, IOptions<JwtOptions> jwt) : IAppointmentLinks
{
    private const int IdLength = 32;
    private const int MacBytes = 16;
    private const int TokenLength = IdLength + (MacBytes * 2);

    public string CreateToken(Guid appointmentId) =>
        appointmentId.ToString("N") + Convert.ToHexStringLower(Mac(tenant.RequireBusinessId(), appointmentId));

    public async Task<ManagedAppointment?> FindByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (token is not { Length: TokenLength } || !Guid.TryParseExact(token[..IdLength], "N", out var id))
        {
            return null;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(token[IdLength..]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(provided, Mac(tenant.RequireBusinessId(), id)))
        {
            return null;
        }

        var appointment = await db.Set<Appointment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        return appointment is null ? null : AppointmentSelfService.ToManaged(appointment);
    }

    private byte[] Mac(Guid businessId, Guid appointmentId)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("appointment-link:" + jwt.Value.SigningKey));
        var data = Encoding.UTF8.GetBytes($"{businessId:N}:{appointmentId:N}");
        return HMACSHA256.HashData(key, data)[..MacBytes];
    }
}

internal sealed class AppointmentSelfService(AppDbContext db, TimeProvider clock) : IAppointmentSelfService
{
    public async Task<ManagedAppointment> RescheduleAsync(
        Guid appointmentId, DateTimeOffset startsAt, CancellationToken cancellationToken = default)
    {
        var appointment = await LoadScheduledAsync(appointmentId, cancellationToken);
        var duration = (int)(appointment.EndsAt - appointment.StartsAt).TotalMinutes;
        appointment.Reschedule(appointment.ProfessionalId, appointment.ServiceId, startsAt, duration,
            new Money(appointment.Price, appointment.Currency), appointment.Notes, clock.GetUtcNow());
        await AppointmentErrors.SaveAsync(db, cancellationToken);
        return ToManaged(appointment);
    }

    public async Task<ManagedAppointment> CancelAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        var appointment = await LoadScheduledAsync(appointmentId, cancellationToken);
        appointment.SetStatus(AppointmentStatus.Cancelled, clock.GetUtcNow());
        await AppointmentErrors.SaveAsync(db, cancellationToken);
        return ToManaged(appointment);
    }

    internal static ManagedAppointment ToManaged(Appointment a) => new(
        a.Id, a.CustomerId, a.ProfessionalId, a.ServiceId, a.Status.ToString(), a.StartsAt, a.EndsAt, a.Price, a.Currency);

    private async Task<Appointment> LoadScheduledAsync(Guid id, CancellationToken cancellationToken)
    {
        var appointment = await db.Set<Appointment>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw AppointmentErrors.NotFound();
        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            throw AppException.Conflict("appointment_not_editable", "Only scheduled appointments can be changed.");
        }

        return appointment;
    }
}
