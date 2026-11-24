using Api.Modules.Businesses.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Businesses.Persistence;

internal sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> builder)
    {
        builder.ToTable("businesses");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Name).HasMaxLength(Business.NameMaxLength).IsRequired();
        builder.Property(b => b.Slug).HasMaxLength(Business.SlugMaxLength).IsRequired();
        builder.HasIndex(b => b.Slug).IsUnique();
        builder.Property(b => b.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(b => b.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(b => b.Phone).HasMaxLength(Business.PhoneMaxLength);
        builder.Property(b => b.CustomerChangeNoticeHours).HasDefaultValue(Business.DefaultChangeNoticeHours);
        builder.Property(b => b.CreatedAt).IsRequired();
    }
}
