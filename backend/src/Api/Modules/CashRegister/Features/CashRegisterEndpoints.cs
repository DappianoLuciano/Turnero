using Api.Modules.Businesses.Contracts;
using Api.Modules.CashRegister.Domain;
using Api.Modules.Payments.Contracts;
using Api.Shared.Auth;
using Api.Shared.Errors;
using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Api.Shared.Time;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Modules.CashRegister.Features;

public sealed record CashMovementResponse(
    Guid Id, CashKind Kind, decimal Amount, string Currency, string Concept, Guid? ReversesId, string? Reason,
    DateTimeOffset CreatedAt)
{
    internal static CashMovementResponse From(CashMovement m) =>
        new(m.Id, m.Kind, m.Amount, m.Currency, m.Concept, m.ReversesId, m.Reason, m.CreatedAt);
}

public sealed record CashClosingResponse(
    Guid Id, DateOnly Date, decimal Expected, decimal Counted, decimal Difference, string Currency, string? Note,
    DateTimeOffset CreatedAt)
{
    internal static CashClosingResponse From(CashClosing c) =>
        new(c.Id, c.Date, c.Expected, c.Counted, c.Difference, c.Currency, c.Note, c.CreatedAt);
}

/// <summary>The cash of one local day: cash collected + manual movements = what should be in the drawer.</summary>
public sealed record CashDayResponse(
    DateOnly Date,
    string Currency,
    decimal CashPayments,
    decimal ManualNet,
    decimal Expected,
    bool IsClosed,
    CashClosingResponse? Closing,
    IReadOnlyList<CashMovementResponse> Movements);

/// <summary>Computes the cash of a local day from the payments ledger and the manual movements (never stored twice).</summary>
internal sealed class CashDays(AppDbContext db, IBusinessDirectory businesses, IPaymentLedger ledger)
{
    public async Task<BusinessSummary> BusinessAsync(CancellationToken cancellationToken) =>
        await businesses.GetCurrentAsync(cancellationToken)
        ?? throw AppException.NotFound("business_not_found", "Business not found.");

    public static DateOnly Today(BusinessSummary business, TimeProvider clock) =>
        DateOnly.FromDateTime(BusinessTimeZone.ToLocal(clock.GetUtcNow(), business.TimeZone).DateTime);

    public async Task<CashDayResponse> GetAsync(DateOnly date, BusinessSummary business, CancellationToken cancellationToken)
    {
        var (from, to) = BusinessTimeZone.LocalDaysToUtc(date, date, business.TimeZone);

        var cashPayments = (await ledger.GetTotalsByMethodAsync(from, to, cancellationToken))
            .Where(t => t.Method == "Cash").Sum(t => t.Total);
        var movements = await db.Set<CashMovement>().AsNoTracking()
            .Where(m => m.CreatedAt >= from && m.CreatedAt < to)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);
        var closing = await db.Set<CashClosing>().AsNoTracking().SingleOrDefaultAsync(c => c.Date == date, cancellationToken);

        var manualNet = movements.Sum(m => m.Amount);
        return new CashDayResponse(
            date, business.Currency, cashPayments, manualNet, cashPayments + manualNet, closing is not null,
            closing is null ? null : CashClosingResponse.From(closing),
            movements.Select(CashMovementResponse.From).ToList());
    }

    /// <summary>Movements cannot be added to a day that was already counted and closed.</summary>
    public async Task EnsureTodayOpenAsync(BusinessSummary business, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = Today(business, clock);
        if (await db.Set<CashClosing>().AnyAsync(c => c.Date == today, cancellationToken))
        {
            throw AppException.Conflict("cash_day_closed", "Today's cash register is already closed.");
        }
    }
}

/// <summary>GET /api/cash-register?date=YYYY-MM-DD: expected cash, movements and closing of one local day.</summary>
internal static class GetCashDay
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/cash-register", HandleAsync)
            .WithName("GetCashDay")
            .WithTags("CashRegister")
            .Produces<CashDayResponse>();

    private static async Task<IResult> HandleAsync(DateOnly date, CashDays days, CancellationToken cancellationToken) =>
        TypedResults.Ok(await days.GetAsync(date, await days.BusinessAsync(cancellationToken), cancellationToken));
}

/// <summary>POST /api/cash-register/movements: manual deposit (income) or withdrawal (expense), any member.</summary>
internal static class RegisterCashMovement
{
    public sealed record RegisterCashMovementRequest(CashKind Kind, decimal Amount, string Concept);

    internal sealed class Validator : AbstractValidator<RegisterCashMovementRequest>
    {
        public Validator()
        {
            RuleFor(r => r.Kind).IsInEnum().WithErrorCode("invalid");
            RuleFor(r => r.Amount).GreaterThan(0).WithErrorCode("invalid")
                .PrecisionScale(Money.Precision, Money.Scale, ignoreTrailingZeros: true).WithErrorCode("invalid");
            RuleFor(r => r.Concept).NotEmpty().WithErrorCode("required")
                .MaximumLength(CashMovement.ConceptMaxLength).WithErrorCode("too_long");
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/cash-register/movements", HandleAsync)
            .WithName("RegisterCashMovement")
            .WithTags("CashRegister")
            .Produces<CashMovementResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        RegisterCashMovementRequest request, AppDbContext db, ITenantContext tenant, CashDays days, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var business = await days.BusinessAsync(cancellationToken);
        await days.EnsureTodayOpenAsync(business, clock, cancellationToken);

        var movement = CashMovement.Register(tenant.RequireBusinessId(), request.Kind,
            new Money(request.Amount, business.Currency), request.Concept, tenant.UserId, clock.GetUtcNow());
        db.Set<CashMovement>().Add(movement);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/cash-register/movements/{movement.Id}", CashMovementResponse.From(movement));
    }
}

/// <summary>POST /api/cash-register/movements/{id}/reversal: cancels a manual movement with an inverse one (owner only).</summary>
internal static class ReverseCashMovement
{
    public sealed record ReverseCashMovementRequest(string Reason);

    internal sealed class Validator : AbstractValidator<ReverseCashMovementRequest>
    {
        public Validator() =>
            RuleFor(r => r.Reason).NotEmpty().WithErrorCode("required")
                .MaximumLength(CashMovement.ReasonMaxLength).WithErrorCode("too_long");
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/cash-register/movements/{id:guid}/reversal", HandleAsync)
            .WithName("ReverseCashMovement")
            .WithTags("CashRegister")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces<CashMovementResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id, ReverseCashMovementRequest request, AppDbContext db, ITenantContext tenant, CashDays days,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var original = await db.Set<CashMovement>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw AppException.NotFound("cash_movement_not_found", "Cash movement not found.");
        if (original.IsReversal)
        {
            throw AppException.Conflict("cash_movement_not_reversible", "A reversal cannot be reversed.");
        }

        if (await db.Set<CashMovement>().AnyAsync(m => m.ReversesId == id, cancellationToken))
        {
            throw AlreadyReversed();
        }

        await days.EnsureTodayOpenAsync(await days.BusinessAsync(cancellationToken), clock, cancellationToken);

        var reversal = CashMovement.Reverse(original, request.Reason, tenant.UserId, clock.GetUtcNow());
        db.Set<CashMovement>().Add(reversal);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw AlreadyReversed();
        }

        return TypedResults.Created($"/api/cash-register/movements/{reversal.Id}", CashMovementResponse.From(reversal));
    }

    private static AppException AlreadyReversed() =>
        AppException.Conflict("cash_movement_already_reversed", "The movement was already reversed.");
}

/// <summary>POST /api/cash-register/closings: counts the drawer and closes a local day (any member).</summary>
internal static class CloseCashDay
{
    public sealed record CloseCashDayRequest(DateOnly Date, decimal CountedAmount, string? Note);

    internal sealed class Validator : AbstractValidator<CloseCashDayRequest>
    {
        public Validator()
        {
            RuleFor(r => r.Date).NotEqual(default(DateOnly)).WithErrorCode("required");
            RuleFor(r => r.CountedAmount).GreaterThanOrEqualTo(0).WithErrorCode("invalid")
                .PrecisionScale(Money.Precision, Money.Scale, ignoreTrailingZeros: true).WithErrorCode("invalid");
            RuleFor(r => r.Note).MaximumLength(CashClosing.NoteMaxLength).WithErrorCode("too_long");
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/cash-register/closings", HandleAsync)
            .WithName("CloseCashDay")
            .WithTags("CashRegister")
            .Produces<CashDayResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        CloseCashDayRequest request, AppDbContext db, ITenantContext tenant, CashDays days, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var business = await days.BusinessAsync(cancellationToken);
        if (request.Date > CashDays.Today(business, clock))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "cash_date_in_future",
                "A future day cannot be closed.");
        }

        if (await db.Set<CashClosing>().AnyAsync(c => c.Date == request.Date, cancellationToken))
        {
            throw AlreadyClosed();
        }

        // What the drawer should hold is computed here, never trusted from the client.
        var expected = (await days.GetAsync(request.Date, business, cancellationToken)).Expected;
        db.Set<CashClosing>().Add(CashClosing.Close(tenant.RequireBusinessId(), request.Date,
            new Money(expected, business.Currency), new Money(request.CountedAmount, business.Currency),
            request.Note, tenant.UserId, clock.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw AlreadyClosed();
        }

        return TypedResults.Created($"/api/cash-register?date={request.Date:yyyy-MM-dd}",
            await days.GetAsync(request.Date, business, cancellationToken));
    }

    private static AppException AlreadyClosed() =>
        AppException.Conflict("cash_day_already_closed", "That day is already closed.");
}
