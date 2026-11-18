using Api.Modules.Services.Domain;
using Api.Shared.Auth;
using Api.Shared.Errors;
using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Services.Features;

public sealed record ServiceResponse(
    Guid Id, string Name, string? Description, int DurationMinutes, decimal Price, string Currency, bool IsActive)
{
    internal static ServiceResponse From(Service s) =>
        new(s.Id, s.Name, s.Description, s.DurationMinutes, s.Price, s.Currency, s.IsActive);
}

/// <summary>GET /api/services: the catalog of the current business (name order).</summary>
internal static class ListServices
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/services", HandleAsync)
            .WithName("ListServices")
            .WithTags("Services")
            .Produces<IReadOnlyList<ServiceResponse>>();

    private static async Task<IResult> HandleAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var services = await db.Set<Service>().AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(services.Select(ServiceResponse.From).ToList());
    }
}

/// <summary>POST /api/services (owner only).</summary>
internal static class CreateService
{
    public sealed record CreateServiceRequest(string Name, string? Description, int DurationMinutes, decimal Price);

    internal sealed class Validator : AbstractValidator<CreateServiceRequest>
    {
        public Validator()
        {
            ServiceRules.Name(RuleFor(r => r.Name));
            ServiceRules.Description(RuleFor(r => r.Description));
            ServiceRules.Duration(RuleFor(r => r.DurationMinutes));
            ServiceRules.Price(RuleFor(r => r.Price));
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/services", HandleAsync)
            .WithName("CreateService")
            .WithTags("Services")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces<ServiceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

    private static async Task<IResult> HandleAsync(
        CreateServiceRequest request, AppDbContext db, ITenantContext tenant, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var service = Service.Create(tenant.RequireBusinessId(), request.Name, request.Description, request.DurationMinutes,
            new Money(request.Price), clock.GetUtcNow());
        db.Set<Service>().Add(service);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/services/{service.Id}", ServiceResponse.From(service));
    }
}

/// <summary>PUT /api/services/{id} (owner only). Also (de)activates the service.</summary>
internal static class UpdateService
{
    public sealed record UpdateServiceRequest(
        string Name, string? Description, int DurationMinutes, decimal Price, bool IsActive);

    internal sealed class Validator : AbstractValidator<UpdateServiceRequest>
    {
        public Validator()
        {
            ServiceRules.Name(RuleFor(r => r.Name));
            ServiceRules.Description(RuleFor(r => r.Description));
            ServiceRules.Duration(RuleFor(r => r.DurationMinutes));
            ServiceRules.Price(RuleFor(r => r.Price));
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPut("/services/{id:guid}", HandleAsync)
            .WithName("UpdateService")
            .WithTags("Services")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces<ServiceResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid id, UpdateServiceRequest request, AppDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var service = await db.Set<Service>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw AppException.NotFound("service_not_found", "Service not found.");
        service.Update(request.Name, request.Description, request.DurationMinutes,
            new Money(request.Price, service.Currency), request.IsActive, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ServiceResponse.From(service));
    }
}

/// <summary>DELETE /api/services/{id} (owner only).</summary>
internal static class DeleteService
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapDelete("/services/{id:guid}", HandleAsync)
            .WithName("DeleteService")
            .WithTags("Services")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        var service = await db.Set<Service>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw AppException.NotFound("service_not_found", "Service not found.");
        db.Set<Service>().Remove(service);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}

internal static class ServiceRules
{
    public static void Name<T>(IRuleBuilderInitial<T, string> rule) =>
        rule.NotEmpty().WithErrorCode("required")
            .MaximumLength(Service.NameMaxLength).WithErrorCode("too_long");

    public static void Description<T>(IRuleBuilderInitial<T, string?> rule) =>
        rule.MaximumLength(Service.DescriptionMaxLength).WithErrorCode("too_long");

    public static void Duration<T>(IRuleBuilderInitial<T, int> rule) =>
        rule.InclusiveBetween(Service.MinDurationMinutes, Service.MaxDurationMinutes).WithErrorCode("out_of_range");

    public static void Price<T>(IRuleBuilderInitial<T, decimal> rule) =>
        rule.GreaterThanOrEqualTo(0).WithErrorCode("out_of_range")
            .LessThan(10_000_000m).WithErrorCode("out_of_range")
            .Must(p => decimal.Round(p, Money.Scale) == p).WithErrorCode("too_many_decimals");
}
