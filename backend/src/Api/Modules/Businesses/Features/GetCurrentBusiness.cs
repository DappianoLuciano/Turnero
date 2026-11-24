using Api.Modules.Businesses.Contracts;
using Api.Modules.Businesses.Domain;
using Api.Shared.Auth;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Businesses.Features;

/// <summary>GET /api/businesses/current: the business the access token is bound to.</summary>
internal static class GetCurrentBusiness
{
    public sealed record CurrentBusinessResponse(
        Guid Id, string Name, string Slug, string TimeZone, string Currency, string? Phone, int CustomerChangeNoticeHours);

    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/businesses/current", HandleAsync)
            .WithName("GetCurrentBusiness")
            .WithTags("Businesses")
            .Produces<CurrentBusinessResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(IBusinessDirectory directory, CancellationToken cancellationToken)
    {
        var business = await directory.GetCurrentAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");
        return TypedResults.Ok(ToResponse(business));
    }

    internal static CurrentBusinessResponse ToResponse(BusinessSummary b) =>
        new(b.Id, b.Name, b.Slug, b.TimeZone, b.Currency, b.Phone, b.CustomerChangeNoticeHours);
}

/// <summary>PUT /api/businesses/current/settings (owner only): contact phone and the notice customers must respect.</summary>
internal static class UpdateBusinessSettings
{
    public sealed record UpdateBusinessSettingsRequest(string? Phone, int CustomerChangeNoticeHours);

    internal sealed class Validator : AbstractValidator<UpdateBusinessSettingsRequest>
    {
        public Validator()
        {
            RuleFor(r => r.Phone).MaximumLength(Business.PhoneMaxLength).WithErrorCode("too_long");
            RuleFor(r => r.CustomerChangeNoticeHours)
                .InclusiveBetween(0, Business.MaxChangeNoticeHours).WithErrorCode("out_of_range");
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPut("/businesses/current/settings", HandleAsync)
            .WithName("UpdateBusinessSettings")
            .WithTags("Businesses")
            .RequireAuthorization(AuthPolicies.Owner)
            .Produces<GetCurrentBusiness.CurrentBusinessResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        UpdateBusinessSettingsRequest request, AppDbContext db, IBusinessDirectory directory,
        CancellationToken cancellationToken)
    {
        // The tenant query filter leaves exactly the current business.
        var business = await db.Set<Business>().SingleOrDefaultAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");
        business.UpdateSettings(request.Phone, request.CustomerChangeNoticeHours);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(GetCurrentBusiness.ToResponse(
            (await directory.GetCurrentAsync(cancellationToken))!));
    }
}
