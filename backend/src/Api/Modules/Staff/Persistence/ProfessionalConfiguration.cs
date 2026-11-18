using Api.Modules.Staff.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Staff.Persistence;

internal sealed class ProfessionalConfiguration : IEntityTypeConfiguration<Professional>
{
    public void Configure(EntityTypeBuilder<Professional> builder)
    {
        builder.ToTable("professionals");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(Professional.NameMaxLength).IsRequired();
        builder.HasIndex(p => new { p.BusinessId, p.Name });
        // FK to businesses is added in the migration with raw SQL: modules do not map other modules' entities.
    }
}

internal sealed class ProfessionalServiceConfiguration : IEntityTypeConfiguration<ProfessionalService>
{
    public void Configure(EntityTypeBuilder<ProfessionalService> builder)
    {
        builder.ToTable("professional_services");
        builder.HasKey(ps => new { ps.ProfessionalId, ps.ServiceId });
        builder.HasIndex(ps => ps.ServiceId);
        // FKs to professionals and services (cascade) are added in the migration with raw SQL.
    }
}
