namespace Api.Shared.Hosting;

/// <summary>Product branding loaded from the repo-root <c>product.json</c> (single source of truth).</summary>
public sealed class ProductInfo
{
    public string Name { get; init; } = "[NOMBRE]";
    public string ShortName { get; init; } = "[NOMBRE]";
    public string Description { get; init; } = string.Empty;
}
