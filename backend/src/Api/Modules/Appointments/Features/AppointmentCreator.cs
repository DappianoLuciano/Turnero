using Api.Modules.Appointments.Contracts;
using Api.Modules.Appointments.Domain;
using Api.Modules.Customers.Contracts;
using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;

namespace Api.Modules.Appointments.Features;

internal sealed class AppointmentCreator(
    AppDbContext db, ITenantContext tenant, ICustomerDirectory customers, AppointmentBooking booking, TimeProvider clock)
    : IAppointmentCreator
{
    public async Task<CreatedAppointment> CreateAsync(
        Guid customerId, Guid professionalId, Guid serviceId, DateTimeOffset startsAt,
        CancellationToken cancellationToken = default)
    {
        var customer = (await customers.GetByIdsAsync([customerId], cancellationToken)).SingleOrDefault()
            ?? throw AppointmentErrors.Invalid("customer_not_found", "Customer not found.");
        if (customer.IsArchived)
        {
            throw AppointmentErrors.Invalid("customer_archived", "The customer is archived.");
        }

        var service = await booking.ResolveAsync(professionalId, serviceId, null, null, cancellationToken);
        var appointment = Appointment.Create(tenant.RequireBusinessId(), customerId, professionalId, serviceId, startsAt,
            service.DurationMinutes, new Money(service.Price, service.Currency), null, clock.GetUtcNow());

        db.Set<Appointment>().Add(appointment);
        await AppointmentErrors.SaveAsync(db, cancellationToken);
        return new CreatedAppointment(appointment.Id, customerId, appointment.StartsAt, appointment.EndsAt,
            appointment.Price, appointment.Currency);
    }
}
