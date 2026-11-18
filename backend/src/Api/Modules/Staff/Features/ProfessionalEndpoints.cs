using Api.Modules.Services.Contracts;
using Api.Modules.Staff.Domain;
using Api.Shared.Auth;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Modules.Staff.Features;

public sealed record ProfessionalResponse(Guid Id, string Name, bool IsActive, IReadOnlyList<Guid> ServiceIds)
{
    internal static ProfessionalResponse From(Professional p, IEnumerable<Guid> serviceIds) =>
        new(p.Id, p.Name, p.IsActive, serviceIds.Order().ToList());
}

/// <summary>GET /api/professionals: professionals of the current business (name order), with their services.</summary>
internal static class ListProfessionals
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/professionals", HandleAsync)
            .WithName("ListProfessionals")
            .WithTags("Staff")
            .Produces<IReadOnlyList<ProfessionalResponse>>();

    private static async Task<IResult> HandleAsync(
        AppDbContext db, CancellationToken cancellationToken, bool includeInactive = true)
    {
        var query = db.Set<Professional>().AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        var professionals = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        var ids = professionals.Select(p => p.Id).ToList();
        var links = await db.Set<ProfessionalService>().AsNoTracking()
            .Where(l => ids.Contains(l.ProfessionalId))
            .ToListAsync(cancellationToken);
        var byProfessional = links.ToLookup(l => l.ProfessionalId, l => l.ServiceId);

        return TypedResults.Ok(professionals.Select(p => ProfessionalResponse.From(p, byProfessional[p.Id])).ToList());
    }
}

/// <summary>POST /api/professionals (owner only).</summary>
internal static class CreateProfessional
{
    public sealed record CreateProfessionalRequest(string Name, IReadOnlyList<Guid> ServiceIds);

    internal sealed class Validator : AbstractValidator<CreateProfessionalRequest>
    {
        public Validator()
        {
            ProfessionalRules.Name(RuleFor(r => r.Name));
            ProfessionalRules.ServiceIds(RuleFor(r => r.ServiceIds));
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/professionals", HandleAsync)
            .WithName("CreateProfessional")
            .WithTags("Staff")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces<ProfessionalResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

    private static async Task<IResult> HandleAsync(
        CreateProfessionalRequest request, AppDbContext db, ITenantContext tenant, IServiceCatalog catalog,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var businessId = tenant.RequireBusinessId();
        var serviceIds = await ProfessionalServices.ResolveAsync(catalog, request.ServiceIds, cancellationToken);

        var professional = Professional.Create(businessId, request.Name, clock.GetUtcNow());
        db.Set<Professional>().Add(professional);
        // Two saves: EF does not know the (raw SQL) FK, so the parent row must exist before its links.
        await db.SaveChangesAsync(cancellationToken);
        foreach (var serviceId in serviceIds)
        {
            db.Set<ProfessionalService>().Add(ProfessionalService.Create(businessId, professional.Id, serviceId));
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/professionals/{professional.Id}",
            ProfessionalResponse.From(professional, serviceIds));
    }
}

/// <summary>PUT /api/professionals/{id} (owner only). Also (de)activates and replaces the set of services.</summary>
internal static class UpdateProfessional
{
    public sealed record UpdateProfessionalRequest(string Name, bool IsActive, IReadOnlyList<Guid> ServiceIds);

    internal sealed class Validator : AbstractValidator<UpdateProfessionalRequest>
    {
        public Validator()
        {
            ProfessionalRules.Name(RuleFor(r => r.Name));
            ProfessionalRules.ServiceIds(RuleFor(r => r.ServiceIds));
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPut("/professionals/{id:guid}", HandleAsync)
            .WithName("UpdateProfessional")
            .WithTags("Staff")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces<ProfessionalResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid id, UpdateProfessionalRequest request, AppDbContext db, ITenantContext tenant, IServiceCatalog catalog,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var businessId = tenant.RequireBusinessId();
        var professional = await db.Set<Professional>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw ProfessionalErrors.NotFound();
        var serviceIds = await ProfessionalServices.ResolveAsync(catalog, request.ServiceIds, cancellationToken);

        professional.Update(request.Name, request.IsActive, clock.GetUtcNow());

        var current = await db.Set<ProfessionalService>().Where(l => l.ProfessionalId == id).ToListAsync(cancellationToken);
        db.Set<ProfessionalService>().RemoveRange(current.Where(l => !serviceIds.Contains(l.ServiceId)));
        var existing = current.Select(l => l.ServiceId).ToHashSet();
        foreach (var serviceId in serviceIds.Where(s => !existing.Contains(s)))
        {
            db.Set<ProfessionalService>().Add(ProfessionalService.Create(businessId, id, serviceId));
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ProfessionalResponse.From(professional, serviceIds));
    }
}

/// <summary>
/// DELETE /api/professionals/{id} (owner only). Permanent. Professionals with history must be deactivated instead:
/// modules that reference professionals add a restrictive FK, which surfaces here as 409.
/// </summary>
internal static class DeleteProfessional
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapDelete("/professionals/{id:guid}", HandleAsync)
            .WithName("DeleteProfessional")
            .WithTags("Staff")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        var professional = await db.Set<Professional>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw ProfessionalErrors.NotFound();
        db.Set<Professional>().Remove(professional);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw AppException.Conflict("professional_has_history",
                "The professional has related records and cannot be deleted. Deactivate it instead.");
        }

        return TypedResults.NoContent();
    }
}

internal static class ProfessionalErrors
{
    public static AppException NotFound() => AppException.NotFound("professional_not_found", "Professional not found.");
}

internal static class ProfessionalServices
{
    /// <summary>Distinct service ids, checked against the catalog of the current business.</summary>
    public static async Task<IReadOnlyList<Guid>> ResolveAsync(
        IServiceCatalog catalog, IReadOnlyList<Guid> requested, CancellationToken cancellationToken)
    {
        var distinct = requested.Distinct().ToList();
        var found = await catalog.GetByIdsAsync(distinct, cancellationToken);
        if (found.Count != distinct.Count)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "service_not_found", "Service not found.");
        }

        return distinct;
    }
}

internal static class ProfessionalRules
{
    public static void Name<T>(IRuleBuilderInitial<T, string> rule) =>
        rule.NotEmpty().WithErrorCode("required")
            .MaximumLength(Professional.NameMaxLength).WithErrorCode("too_long");

    public static void ServiceIds<T>(IRuleBuilderInitial<T, IReadOnlyList<Guid>> rule) =>
        rule.NotNull().WithErrorCode("required")
            .Must(ids => ids is null || ids.Count <= Professional.MaxServices).WithErrorCode("too_many")
            .Must(ids => ids is null || ids.All(id => id != Guid.Empty)).WithErrorCode("invalid");
}
