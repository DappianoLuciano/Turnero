using Api.Modules.Schedules.Domain;
using Api.Modules.Staff.Contracts;
using Api.Shared.Auth;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Modules.Schedules.Features;

public sealed record WorkingBlockResponse(Guid ProfessionalId, int DayOfWeek, int StartMinute, int EndMinute);

public sealed record ScheduleResponse(IReadOnlyList<WorkingBlockResponse> Blocks);

public sealed record ScheduleExceptionResponse(Guid Id, Guid? ProfessionalId, DateOnly Date, string? Reason)
{
    internal static ScheduleExceptionResponse From(ScheduleException e) => new(e.Id, e.ProfessionalId, e.Date, e.Reason);
}

/// <summary>GET /api/schedules: the weekly working hours of every professional of the business.</summary>
internal static class GetSchedules
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/schedules", HandleAsync)
            .WithName("GetSchedules")
            .WithTags("Schedules")
            .Produces<ScheduleResponse>();

    private static async Task<IResult> HandleAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var blocks = await db.Set<WorkingBlock>().AsNoTracking()
            .OrderBy(b => b.ProfessionalId).ThenBy(b => b.DayOfWeek).ThenBy(b => b.StartMinute)
            .Select(b => new WorkingBlockResponse(b.ProfessionalId, b.DayOfWeek, b.StartMinute, b.EndMinute))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new ScheduleResponse(blocks));
    }
}

/// <summary>PUT /api/schedules/professionals/{id}: replaces the whole weekly schedule of a professional (owner only).</summary>
internal static class ReplaceProfessionalSchedule
{
    public const int MaxBlocks = 50;

    public sealed record BlockInput(int DayOfWeek, int StartMinute, int EndMinute);

    public sealed record ReplaceProfessionalScheduleRequest(IReadOnlyList<BlockInput> Blocks);

    internal sealed class Validator : AbstractValidator<ReplaceProfessionalScheduleRequest>
    {
        public Validator()
        {
            RuleFor(r => r.Blocks).NotNull().WithErrorCode("required")
                .Must(blocks => blocks is null || blocks.Count <= MaxBlocks).WithErrorCode("too_many");
            RuleForEach(r => r.Blocks).ChildRules(block =>
            {
                block.RuleFor(b => b.DayOfWeek).InclusiveBetween(0, 6).WithErrorCode("invalid");
                block.RuleFor(b => b.StartMinute).InclusiveBetween(0, WorkingBlock.MinutesPerDay - 1).WithErrorCode("invalid");
                block.RuleFor(b => b.EndMinute).InclusiveBetween(1, WorkingBlock.MinutesPerDay).WithErrorCode("invalid");
                block.RuleFor(b => b).Must(b => b.StartMinute < b.EndMinute).WithErrorCode("invalid").WithName("block");
            }).When(r => r.Blocks is not null);
            RuleFor(r => r.Blocks).Must(NotOverlap).WithErrorCode("overlap")
                .When(r => r.Blocks is { Count: <= MaxBlocks });
        }

        private static bool NotOverlap(IReadOnlyList<BlockInput> blocks) =>
            blocks.GroupBy(b => b.DayOfWeek).All(day =>
            {
                var ordered = day.OrderBy(b => b.StartMinute).ToList();
                return ordered.Zip(ordered.Skip(1)).All(pair => pair.First.EndMinute <= pair.Second.StartMinute);
            });
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPut("/schedules/professionals/{professionalId:guid}", HandleAsync)
            .WithName("ReplaceProfessionalSchedule")
            .WithTags("Schedules")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces<ScheduleResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid professionalId, ReplaceProfessionalScheduleRequest request, AppDbContext db, ITenantContext tenant,
        IProfessionalDirectory professionals, CancellationToken cancellationToken)
    {
        await ScheduleErrors.EnsureProfessionalAsync(professionals, professionalId, cancellationToken);

        // Delete first (statement by statement): the exclusion constraint is checked on every row.
        await db.Set<WorkingBlock>().Where(b => b.ProfessionalId == professionalId).ExecuteDeleteAsync(cancellationToken);
        var businessId = tenant.RequireBusinessId();
        foreach (var block in request.Blocks)
        {
            db.Set<WorkingBlock>().Add(WorkingBlock.Create(
                businessId, professionalId, block.DayOfWeek, block.StartMinute, block.EndMinute));
        }

        await db.SaveChangesAsync(cancellationToken);
        var blocks = await db.Set<WorkingBlock>().AsNoTracking().Where(b => b.ProfessionalId == professionalId)
            .OrderBy(b => b.DayOfWeek).ThenBy(b => b.StartMinute)
            .Select(b => new WorkingBlockResponse(b.ProfessionalId, b.DayOfWeek, b.StartMinute, b.EndMinute))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new ScheduleResponse(blocks));
    }
}

/// <summary>GET /api/schedules/exceptions?from=&amp;to=: closed days (holidays, days off) in a range of local dates.</summary>
internal static class ListScheduleExceptions
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/schedules/exceptions", HandleAsync)
            .WithName("ListScheduleExceptions")
            .WithTags("Schedules")
            .Produces<IReadOnlyList<ScheduleExceptionResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> HandleAsync(
        DateOnly from, DateOnly to, AppDbContext db, CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "invalid_range",
                "The range must be valid and span at most a year.");
        }

        var items = await db.Set<ScheduleException>().AsNoTracking()
            .Where(e => e.Date >= from && e.Date <= to)
            .OrderBy(e => e.Date).ThenBy(e => e.ProfessionalId)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(items.Select(ScheduleExceptionResponse.From).ToList());
    }
}

/// <summary>POST /api/schedules/exceptions: closes a local day for the business or one professional (owner only).</summary>
internal static class CreateScheduleException
{
    public sealed record CreateScheduleExceptionRequest(DateOnly Date, Guid? ProfessionalId, string? Reason);

    internal sealed class Validator : AbstractValidator<CreateScheduleExceptionRequest>
    {
        public Validator()
        {
            RuleFor(r => r.Date).NotEqual(default(DateOnly)).WithErrorCode("required");
            RuleFor(r => r.Reason).MaximumLength(ScheduleException.ReasonMaxLength).WithErrorCode("too_long");
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/schedules/exceptions", HandleAsync)
            .WithName("CreateScheduleException")
            .WithTags("Schedules")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces<ScheduleExceptionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        CreateScheduleExceptionRequest request, AppDbContext db, ITenantContext tenant,
        IProfessionalDirectory professionals, CancellationToken cancellationToken)
    {
        if (request.ProfessionalId is { } professionalId)
        {
            await ScheduleErrors.EnsureProfessionalAsync(professionals, professionalId, cancellationToken);
        }

        var exception = ScheduleException.Create(
            tenant.RequireBusinessId(), request.ProfessionalId, request.Date, request.Reason);
        db.Set<ScheduleException>().Add(exception);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw AppException.Conflict("schedule_exception_exists", "That day is already closed.");
        }

        return TypedResults.Created($"/api/schedules/exceptions/{exception.Id}", ScheduleExceptionResponse.From(exception));
    }
}

/// <summary>DELETE /api/schedules/exceptions/{id}: reopens a closed day (owner only).</summary>
internal static class DeleteScheduleException
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapDelete("/schedules/exceptions/{id:guid}", HandleAsync)
            .WithName("DeleteScheduleException")
            .WithTags("Schedules")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        var exception = await db.Set<ScheduleException>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw AppException.NotFound("schedule_exception_not_found", "Closed day not found.");
        db.Set<ScheduleException>().Remove(exception);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}

internal static class ScheduleErrors
{
    public static async Task EnsureProfessionalAsync(
        IProfessionalDirectory professionals, Guid professionalId, CancellationToken cancellationToken)
    {
        if ((await professionals.GetByIdsAsync([professionalId], cancellationToken)).Count == 0)
        {
            throw AppException.NotFound("professional_not_found", "Professional not found.");
        }
    }
}
