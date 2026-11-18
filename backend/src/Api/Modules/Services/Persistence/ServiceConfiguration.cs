using Api.Modules.Services.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Services.Persistence;

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(Service.NameMaxLength).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(Service.DescriptionMaxLength);
        builder.Property(s => s.Currency).HasColumnType("char(3)").IsRequired();
        builder.HasIndex(s => new { s.BusinessId, s.Name });
        // FK to businesses is added in the migration with raw SQL: modules do not map other modules' entities.
    }
}
