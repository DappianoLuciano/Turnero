using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Api.Shared.Auth;

public sealed class JwtOptions : IValidatableObject
{
    public const string Section = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 key. At least 32 bytes. Never committed: env var / user-secrets.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            yield return new ValidationResult("Jwt:SigningKey must be at least 32 bytes.", [nameof(SigningKey)]);
        }
    }
}
