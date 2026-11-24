using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Api.Modules.Businesses.Contracts;
using Api.Modules.Users.Contracts;
using Api.Shared.Errors;
using Api.Shared.Tenancy;
using FluentValidation;

namespace Api.Modules.Signup.Features;

public sealed record SignupResponse(Guid BusinessId, string Slug);

/// <summary>
/// POST /api/signup: anyone can create a business and its owner account. The business starts with a free trial
/// (Subscriptions module). Returns no session: the client logs in with the credentials it just registered.
/// </summary>
internal static partial class SignupBusiness
{
    public const int PasswordMinLength = 8;
    private const int NameMaxLength = 120;
    private const int MaxSlugAttempts = 5;
    private const int SlugBaseMaxLength = 50;

    public sealed record SignupRequest(string BusinessName, string FullName, string Email, string Password);

    internal sealed class Validator : AbstractValidator<SignupRequest>
    {
        public Validator()
        {
            RuleFor(r => r.BusinessName).NotEmpty().WithErrorCode("required")
                .MaximumLength(NameMaxLength).WithErrorCode("too_long");
            RuleFor(r => r.FullName).NotEmpty().WithErrorCode("required")
                .MaximumLength(120).WithErrorCode("too_long");
            RuleFor(r => r.Email).NotEmpty().WithErrorCode("required")
                .MaximumLength(254).WithErrorCode("too_long")
                .EmailAddress().WithErrorCode("invalid_email");
            RuleFor(r => r.Password).NotEmpty().WithErrorCode("required")
                .MinimumLength(PasswordMinLength).WithErrorCode("too_short")
                .MaximumLength(128).WithErrorCode("too_long");
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/signup", HandleAsync)
            .WithName("Signup")
            .WithTags("Signup")
            .RequireRateLimiting(PublicRateLimiting.SignupPolicy)
            .Produces<SignupResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

    private static async Task<IResult> HandleAsync(
        SignupRequest request, IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        // Each step uses a fresh scope: TenantScope needs a clean DbContext/tenant per unit of work.
        await using (var check = scopes.CreateAsyncScope())
        {
            if (await check.ServiceProvider.GetRequiredService<IUserProvisioning>()
                    .EmailExistsAsync(request.Email, cancellationToken))
            {
                throw EmailTaken();
            }
        }

        var business = await CreateBusinessAsync(request.BusinessName, scopes, cancellationToken);

        await using (var owner = scopes.CreateAsyncScope())
        {
            await owner.ServiceProvider.GetRequiredService<IUserProvisioning>()
                .RegisterOwnerAsync(business.Id, request.Email, request.FullName, request.Password, cancellationToken);
        }

        return TypedResults.Created($"/api/businesses/current", new SignupResponse(business.Id, business.Slug));
    }

    private static async Task<BusinessSummary> CreateBusinessAsync(
        string name, IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        var baseSlug = Slugify(name);
        for (var attempt = 0; attempt < MaxSlugAttempts; attempt++)
        {
            var slug = attempt == 0 ? baseSlug : $"{baseSlug}-{Guid.NewGuid().ToString("N")[..4]}";
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                return await scope.ServiceProvider.GetRequiredService<IBusinessProvisioning>()
                    .CreateAsync(name, slug, cancellationToken: cancellationToken);
            }
            catch (AppException ex) when (ex.Code == "slug_taken")
            {
                // Try again with a random suffix.
            }
        }

        throw AppException.Conflict("slug_taken", "Could not find a free address for the business.");
    }

    private static AppException EmailTaken() =>
        AppException.Conflict("email_taken", "The email is already registered.");

    /// <summary>"Barbería Don José!" → "barberia-don-jose". Always a valid DNS label (falls back to "negocio").</summary>
    public static string Slugify(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var ascii = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                ascii.Append(c);
            }
        }

        var slug = NonSlugChars().Replace(ascii.ToString().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > SlugBaseMaxLength)
        {
            slug = slug[..SlugBaseMaxLength].TrimEnd('-');
        }

        return slug.Length == 0 ? "negocio" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();
}
