using Api.Modules.Businesses.Contracts;
using Api.Shared.Errors;

namespace Api.Modules.Businesses.Features;

/// <summary>GET /api/businesses/current: the business the access token is bound to.</summary>
internal static class GetCurrentBusiness
{
    public sealed record Response(Guid Id, string Name, string Slug, string TimeZone, string Currency);

    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/businesses/current", HandleAsync)
            .WithName("GetCurrentBusiness")
            .WithTags("Businesses")
            .Produces<Response>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(IBusinessDirectory directory, CancellationToken cancellationToken)
    {
        var business = await directory.GetCurrentAsync(cancellationToken)
            ?? throw AppException.NotFound("business_not_found", "Business not found.");
        return TypedResults.Ok(new Response(business.Id, business.Name, business.Slug, business.TimeZone, business.Currency));
    }
}
