using System.ComponentModel.DataAnnotations;
using Api.Modules.Customers.Domain;
using Api.Shared.Auth;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Modules.Customers.Features;

public sealed record CustomerResponse(
    Guid Id, string Name, string? Phone, string? Email, string? Notes, bool IsArchived)
{
    internal static CustomerResponse From(Customer c) => new(c.Id, c.Name, c.Phone, c.Email, c.Notes, c.IsArchived);
}

public sealed record CustomerPage(IReadOnlyList<CustomerResponse> Items, int Total, int Page, int PageSize);

/// <summary>GET /api/customers?search=&amp;includeArchived=&amp;page=&amp;pageSize=: name order.</summary>
internal static class ListCustomers
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/customers", HandleAsync)
            .WithName("ListCustomers")
            .WithTags("Customers")
            .Produces<CustomerPage>();

    private static async Task<IResult> HandleAsync(
        AppDbContext db, CancellationToken cancellationToken,
        string? search = null, bool includeArchived = false, int page = 1, int pageSize = DefaultPageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Set<Customer>().AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(c => !c.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var pattern = $"%{EscapeLike(term)}%";
            // Phones are stored normalized, so match the normalized term against them.
            var phonePattern = Customer.NormalizePhone(term) is { Length: > 0 } digits
                ? $"%{EscapeLike(digits)}%"
                : pattern;
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, pattern, "\\")
                || (c.Phone != null && EF.Functions.ILike(c.Phone, phonePattern, "\\"))
                || (c.Email != null && EF.Functions.ILike(c.Email, pattern, "\\")));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new CustomerPage(items.Select(CustomerResponse.From).ToList(), total, page, pageSize));
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

/// <summary>GET /api/customers/{id}.</summary>
internal static class GetCustomer
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/customers/{id:guid}", HandleAsync)
            .WithName("GetCustomer")
            .WithTags("Customers")
            .Produces<CustomerResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        var customer = await db.Set<Customer>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw CustomerErrors.NotFound();
        return TypedResults.Ok(CustomerResponse.From(customer));
    }
}

/// <summary>POST /api/customers (any member of the business).</summary>
internal static class CreateCustomer
{
    public sealed record CreateCustomerRequest(string Name, string? Phone, string? Email, string? Notes);

    internal sealed class Validator : AbstractValidator<CreateCustomerRequest>
    {
        public Validator()
        {
            CustomerRules.Name(RuleFor(r => r.Name));
            CustomerRules.Phone(RuleFor(r => r.Phone));
            CustomerRules.Email(RuleFor(r => r.Email));
            CustomerRules.Notes(RuleFor(r => r.Notes));
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/customers", HandleAsync)
            .WithName("CreateCustomer")
            .WithTags("Customers")
            .Produces<CustomerResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        CreateCustomerRequest request, AppDbContext db, ITenantContext tenant, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var customer = Customer.Create(tenant.RequireBusinessId(), request.Name, request.Phone, request.Email,
            request.Notes, clock.GetUtcNow());
        await CustomerErrors.EnsurePhoneAvailableAsync(db, customer.Phone, customer.Id, cancellationToken);
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/customers/{customer.Id}", CustomerResponse.From(customer));
    }
}

/// <summary>PUT /api/customers/{id} (any member). Also archives or restores the customer.</summary>
internal static class UpdateCustomer
{
    public sealed record UpdateCustomerRequest(string Name, string? Phone, string? Email, string? Notes, bool IsArchived);

    internal sealed class Validator : AbstractValidator<UpdateCustomerRequest>
    {
        public Validator()
        {
            CustomerRules.Name(RuleFor(r => r.Name));
            CustomerRules.Phone(RuleFor(r => r.Phone));
            CustomerRules.Email(RuleFor(r => r.Email));
            CustomerRules.Notes(RuleFor(r => r.Notes));
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPut("/customers/{id:guid}", HandleAsync)
            .WithName("UpdateCustomer")
            .WithTags("Customers")
            .Produces<CustomerResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id, UpdateCustomerRequest request, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var customer = await db.Set<Customer>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw CustomerErrors.NotFound();
        customer.Update(request.Name, request.Phone, request.Email, request.Notes, request.IsArchived,
            clock.GetUtcNow());
        if (!customer.IsArchived)
        {
            await CustomerErrors.EnsurePhoneAvailableAsync(db, customer.Phone, customer.Id, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(CustomerResponse.From(customer));
    }
}

/// <summary>
/// DELETE /api/customers/{id} (owner only). Permanent. Customers with history must be archived instead:
/// modules that reference customers add a restrictive FK, which surfaces here as 409.
/// </summary>
internal static class DeleteCustomer
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapDelete("/customers/{id:guid}", HandleAsync)
            .WithName("DeleteCustomer")
            .WithTags("Customers")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        var customer = await db.Set<Customer>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw CustomerErrors.NotFound();
        db.Set<Customer>().Remove(customer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw AppException.Conflict("customer_has_history",
                "The customer has related records and cannot be deleted. Archive it instead.");
        }

        return TypedResults.NoContent();
    }
}

internal static class CustomerErrors
{
    public static AppException NotFound() => AppException.NotFound("customer_not_found", "Customer not found.");

    /// <summary>Friendly 409 before the unique index (active customers only) rejects the write.</summary>
    public static async Task EnsurePhoneAvailableAsync(
        AppDbContext db, string? phone, Guid customerId, CancellationToken cancellationToken)
    {
        if (phone is null)
        {
            return;
        }

        var taken = await db.Set<Customer>().AnyAsync(
            c => c.Phone == phone && !c.IsArchived && c.Id != customerId, cancellationToken);
        if (taken)
        {
            throw AppException.Conflict("customer_phone_taken", "A customer with this phone already exists.");
        }
    }
}

internal static class CustomerRules
{
    public static void Name<T>(IRuleBuilderInitial<T, string> rule) =>
        rule.NotEmpty().WithErrorCode("required")
            .MaximumLength(Customer.NameMaxLength).WithErrorCode("too_long");

    public static void Phone<T>(IRuleBuilderInitial<T, string?> rule) =>
        rule.Must(Customer.IsValidPhone).WithErrorCode("invalid_phone");

    public static void Email<T>(IRuleBuilderInitial<T, string?> rule) =>
        rule.MaximumLength(Customer.EmailMaxLength).WithErrorCode("too_long")
            .Must(email => string.IsNullOrWhiteSpace(email) || new EmailAddressAttribute().IsValid(email.Trim()))
            .WithErrorCode("invalid_email");

    public static void Notes<T>(IRuleBuilderInitial<T, string?> rule) =>
        rule.MaximumLength(Customer.NotesMaxLength).WithErrorCode("too_long");
}
