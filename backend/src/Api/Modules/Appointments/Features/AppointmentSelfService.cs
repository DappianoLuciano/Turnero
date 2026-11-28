using System.ComponentModel.DataAnnotations;
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
/// Key of the customer links. Its own secret (<c>AppointmentLinks:SigningKey</c>) so the JWT key can be rotated
/// without breaking the links already sent by WhatsApp. Optional while migrating: without it, links are signed with a key
/// derived from the JWT key (the original behavior), and links signed that way keep validating after it is configured.
/// </summary>
internal sealed class AppointmentLinkOptions : IValidatableObject
{
    public const string Section = "AppointmentLinks";

    public string? SigningKey { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(SigningKey) && Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            yield return new ValidationResult(
                "AppointmentLinks:SigningKey must be at least 32 bytes.", [nameof(SigningKey)]);
        }
    }
}

/// <summary>
/// Token = appointment id (32 hex) + first 16 bytes of HMAC-SHA256(business id, appointment id) (32 hex), keyed with
/// <see cref="AppointmentLinkOptions"/> (domain separated). Only the server can issue it; a token of one business never
/// validates in another because the business id is part of the signed data.
/// </summary>
internal sealed class AppointmentLinks(
    AppDbContext db, ITenantContext tenant, IOptions<AppointmentLinkOptions> links, IOptions<JwtOptions> jwt)
    : IAppointmentLinks
{
    private const int IdLength = 32;
    private const int MacBytes = 16;
    private const int TokenLength = IdLength + (MacBytes * 2);

    public string CreateToken(Guid appointmentId) =>
        appointmentId.ToString("N") + Convert.ToHexStringLower(Mac(Keys()[0], tenant.RequireBusinessId(), appointmentId));

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

        var businessId = tenant.RequireBusinessId();
        if (!Keys().Any(key => CryptographicOperations.FixedTimeEquals(provided, Mac(key, businessId, id))))
        {
            return null;
        }

        var appointment = await db.Set<Appointment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        return appointment is null ? null : AppointmentSelfService.ToManaged(appointment);
    }

    /// <summary>Signing key first; the legacy JWT-derived key last, only to validate links issued before.</summary>
    private byte[][] Keys()
    {
        var legacy = SHA256.HashData(Encoding.UTF8.GetBytes("appointment-link:" + jwt.Value.SigningKey));
        var own = links.Value.SigningKey;
        return string.IsNullOrEmpty(own)
            ? [legacy]
            : [SHA256.HashData(Encoding.UTF8.GetBytes("appointment-link-v2:" + own)), legacy];
    }

    private static byte[] Mac(byte[] key, Guid businessId, Guid appointmentId)
    {
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
