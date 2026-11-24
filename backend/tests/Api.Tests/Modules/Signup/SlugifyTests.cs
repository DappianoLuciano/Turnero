using Api.Modules.Businesses.Domain;
using Api.Modules.Signup.Features;

namespace Api.Tests.Modules.Signup;

public sealed class SlugifyTests
{
    [Theory]
    [InlineData("Barbería Don José!", "barberia-don-jose")]
    [InlineData("  Uñas & Estética  ", "unas-estetica")]
    [InlineData("---", "negocio")]
    [InlineData("日本", "negocio")]
    public void Builds_a_valid_slug(string name, string expected)
    {
        var slug = SignupBusiness.Slugify(name);

        slug.ShouldBe(expected);
        Business.IsValidSlug(slug).ShouldBeTrue();
    }

    [Fact]
    public void Long_names_are_truncated_without_a_trailing_hyphen()
    {
        var slug = SignupBusiness.Slugify(new string('a', 49) + " bbbb");

        slug.Length.ShouldBeLessThanOrEqualTo(50);
        slug.ShouldNotEndWith("-");
        Business.IsValidSlug(slug).ShouldBeTrue();
    }
}
