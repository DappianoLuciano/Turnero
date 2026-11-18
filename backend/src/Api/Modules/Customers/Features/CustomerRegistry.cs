using Api.Modules.Customers.Contracts;
using Api.Modules.Customers.Domain;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Modules.Customers.Features;

internal sealed class CustomerRegistry(AppDbContext db, ITenantContext tenant, TimeProvider clock) : ICustomerRegistry
{
    public async Task<CustomerSummary> FindOrCreateByPhoneAsync(
        string name, string phone, CancellationToken cancellationToken = default)
    {
        var normalized = Customer.NormalizePhone(phone);
        if (normalized is null || !Customer.IsValidPhone(normalized))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "invalid_phone", "The phone number is not valid.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > Customer.NameMaxLength)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "invalid_name", "The name is not valid.");
        }

        var existing = await db.Set<Customer>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Phone == normalized && !c.IsArchived, cancellationToken);
        if (existing is not null)
        {
            return new CustomerSummary(existing.Id, existing.Name, existing.Phone, existing.IsArchived);
        }

        var customer = Customer.Create(tenant.RequireBusinessId(), name, normalized, null, null, clock.GetUtcNow());
        db.Set<Customer>().Add(customer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two bookings for a brand-new phone raced: the unique index let only one customer through.
            throw AppException.Conflict("customer_phone_taken", "A customer with that phone already exists.");
        }

        return new CustomerSummary(customer.Id, customer.Name, customer.Phone, customer.IsArchived);
    }
}
