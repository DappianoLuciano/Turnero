using Api.Modules.Appointments.Contracts;
using Api.Modules.Businesses.Contracts;
using Api.Modules.Customers.Contracts;
using Api.Modules.Payments.Domain;
using Api.Shared.Auth;
using Api.Shared.Errors;
using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Api.Shared.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Modules.Payments.Features;

public sealed record PaymentResponse(
    Guid Id,
    Guid AppointmentId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    Guid? ReversesId,
    string? Reason,
    DateTimeOffset CreatedAt)
{
    internal static PaymentResponse From(Payment p) =>
        new(p.Id, p.AppointmentId, p.Amount, p.Currency, p.Method, p.ReversesId, p.Reason, p.CreatedAt);
}

/// <summary>What an appointment costs, what was collected (net of reversals) and what is still owed.</summary>
public sealed record AppointmentPaymentsResponse(
    Guid AppointmentId,
    decimal Price,
    string Currency,
    decimal Paid,
    decimal Balance,
    bool AcceptsPayments,
    IReadOnlyList<PaymentResponse> Payments);

/// <summary>GET /api/payments?appointmentId=: the payments of one appointment, oldest first, with the balance.</summary>
internal static class ListAppointmentPayments
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/payments", HandleAsync)
            .WithName("ListAppointmentPayments")
            .WithTags("Payments")
            .Produces<AppointmentPaymentsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid appointmentId, AppDbContext db, IAppointmentLookup appointments, CancellationToken cancellationToken)
    {
        var appointment = await PaymentRules.FindAppointmentAsync(appointments, appointmentId, cancellationToken);
        var payments = await db.Set<Payment>().AsNoTracking()
            .Where(p => p.AppointmentId == appointmentId)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(PaymentRules.Summarize(appointment, payments));
    }
}

public sealed record DailyPaymentItem(PaymentResponse Payment, string CustomerName);

public sealed record MethodTotal(PaymentMethod Method, decimal Total);

/// <summary>The money of one local day: net total, net total by method and every movement (reversals included).</summary>
public sealed record DailyPaymentsResponse(
    string Currency, decimal Total, IReadOnlyList<MethodTotal> ByMethod, IReadOnlyList<DailyPaymentItem> Items);

/// <summary>GET /api/payments/daily?date=YYYY-MM-DD: movements registered during one local day of the business.</summary>
internal static class ListDailyPayments
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/payments/daily", HandleAsync)
            .WithName("ListDailyPayments")
            .WithTags("Payments")
            .Produces<DailyPaymentsResponse>();

    private static async Task<IResult> HandleAsync(
        DateOnly date, AppDbContext db, IBusinessDirectory businesses, IAppointmentLookup appointments,
        ICustomerDirectory customers, CancellationToken cancellationToken)
    {
        var business = await businesses.GetCurrentAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");
        var from = BusinessTimeZone.ToUtc(date.ToDateTime(TimeOnly.MinValue), business.TimeZone);
        var to = BusinessTimeZone.ToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), business.TimeZone);

        var payments = await db.Set<Payment>().AsNoTracking()
            .Where(p => p.CreatedAt >= from && p.CreatedAt < to)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        var appointmentById = (await appointments.GetByIdsAsync(
                payments.Select(p => p.AppointmentId).Distinct().ToList(), cancellationToken))
            .ToDictionary(a => a.Id);
        var customerName = (await customers.GetByIdsAsync(
                appointmentById.Values.Select(a => a.CustomerId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.Id, c => c.Name);

        var items = payments.Select(p => new DailyPaymentItem(
            PaymentResponse.From(p),
            appointmentById.TryGetValue(p.AppointmentId, out var a)
                ? customerName.GetValueOrDefault(a.CustomerId, string.Empty)
                : string.Empty)).ToList();

        var byMethod = payments.GroupBy(p => p.Method)
            .Select(g => new MethodTotal(g.Key, g.Sum(p => p.Amount)))
            .OrderBy(m => m.Method).ToList();

        return TypedResults.Ok(new DailyPaymentsResponse(
            business.Currency, payments.Sum(p => p.Amount), byMethod, items));
    }
}

/// <summary>POST /api/payments: collects part or all of the balance of an appointment (any member).</summary>
internal static class CollectPayment
{
    public sealed record CollectPaymentRequest(Guid AppointmentId, decimal Amount, PaymentMethod Method);

    internal sealed class Validator : AbstractValidator<CollectPaymentRequest>
    {
        public Validator()
        {
            RuleFor(r => r.AppointmentId).NotEmpty().WithErrorCode("required");
            RuleFor(r => r.Amount).GreaterThan(0).WithErrorCode("invalid")
                .PrecisionScale(Money.Precision, Money.Scale, ignoreTrailingZeros: true).WithErrorCode("invalid");
            RuleFor(r => r.Method).IsInEnum().WithErrorCode("invalid");
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/payments", HandleAsync)
            .WithName("CollectPayment")
            .WithTags("Payments")
            .Produces<AppointmentPaymentsResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        CollectPaymentRequest request, AppDbContext db, ITenantContext tenant, IAppointmentLookup appointments,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        // Serialize collections of the same appointment: without the lock, two simultaneous payments both read the
        // same balance and together could exceed the price.
        await appointments.LockAsync(request.AppointmentId, cancellationToken);
        var appointment = await PaymentRules.FindAppointmentAsync(appointments, request.AppointmentId, cancellationToken);
        if (!appointment.AcceptsPayments)
        {
            throw AppException.Conflict("appointment_not_payable", "Cancelled appointments cannot be paid.");
        }

        var existing = await db.Set<Payment>().Where(p => p.AppointmentId == appointment.Id)
            .ToListAsync(cancellationToken);
        var balance = appointment.Price - existing.Sum(p => p.Amount);
        if (request.Amount > balance)
        {
            throw AppException.Conflict("payment_exceeds_balance", "The amount is higher than the pending balance.");
        }

        var payment = Payment.Collect(tenant.RequireBusinessId(), appointment.Id,
            new Money(request.Amount, appointment.Currency), request.Method, tenant.UserId, clock.GetUtcNow());
        db.Set<Payment>().Add(payment);
        await db.SaveChangesAsync(cancellationToken);

        existing.Add(payment);
        return TypedResults.Created($"/api/payments?appointmentId={appointment.Id}",
            PaymentRules.Summarize(appointment, existing.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToList()));
    }
}

/// <summary>POST /api/payments/{id}/reversal: cancels a payment with an inverse movement (owner only).</summary>
internal static class ReversePayment
{
    public sealed record ReversePaymentRequest(string Reason);

    internal sealed class Validator : AbstractValidator<ReversePaymentRequest>
    {
        public Validator() =>
            RuleFor(r => r.Reason).NotEmpty().WithErrorCode("required")
                .MaximumLength(Payment.ReasonMaxLength).WithErrorCode("too_long");
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/payments/{id:guid}/reversal", HandleAsync)
            .WithName("ReversePayment")
            .WithTags("Payments")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces<AppointmentPaymentsResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id, ReversePaymentRequest request, AppDbContext db, ITenantContext tenant, IAppointmentLookup appointments,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var original = await db.Set<Payment>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw AppException.NotFound("payment_not_found", "Payment not found.");
        if (original.IsReversal)
        {
            throw AppException.Conflict("payment_not_reversible", "A reversal cannot be reversed.");
        }

        if (await db.Set<Payment>().AnyAsync(p => p.ReversesId == id, cancellationToken))
        {
            throw AlreadyReversed();
        }

        db.Set<Payment>().Add(Payment.Reverse(original, request.Reason, tenant.UserId, clock.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two reversals raced: the unique index on reverses_id lets only one through.
            throw AlreadyReversed();
        }

        var appointment = await PaymentRules.FindAppointmentAsync(appointments, original.AppointmentId, cancellationToken);
        var payments = await db.Set<Payment>().AsNoTracking().Where(p => p.AppointmentId == original.AppointmentId)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        return TypedResults.Created($"/api/payments?appointmentId={original.AppointmentId}",
            PaymentRules.Summarize(appointment, payments));
    }

    private static AppException AlreadyReversed() =>
        AppException.Conflict("payment_already_reversed", "The payment was already reversed.");
}

internal static class PaymentRules
{
    public static async Task<AppointmentSummary> FindAppointmentAsync(
        IAppointmentLookup appointments, Guid appointmentId, CancellationToken cancellationToken) =>
        (await appointments.GetByIdsAsync([appointmentId], cancellationToken)).SingleOrDefault()
        ?? throw AppException.NotFound("appointment_not_found", "Appointment not found.");

    public static AppointmentPaymentsResponse Summarize(AppointmentSummary appointment, IReadOnlyList<Payment> payments)
    {
        var paid = payments.Sum(p => p.Amount);
        return new AppointmentPaymentsResponse(
            appointment.Id, appointment.Price, appointment.Currency, paid, appointment.Price - paid,
            appointment.AcceptsPayments, payments.Select(PaymentResponse.From).ToList());
    }
}
