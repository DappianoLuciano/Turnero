using Api.Modules.Appointments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Appointments.Persistence;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(a => a.Notes).HasMaxLength(Appointment.NotesMaxLength);
        builder.HasIndex(a => new { a.BusinessId, a.StartsAt });
        builder.HasIndex(a => new { a.ProfessionalId, a.StartsAt });
        builder.HasIndex(a => a.CustomerId);
        builder.HasIndex(a => a.ServiceId);
        // FKs (businesses cascade; customers, professionals and services restrict) and the exclusion constraint
        // against overlapping appointments are added in the migration with raw SQL.
    }
}
