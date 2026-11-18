using Api.Modules.Schedules.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Schedules.Persistence;

internal sealed class WorkingBlockConfiguration : IEntityTypeConfiguration<WorkingBlock>
{
    public void Configure(EntityTypeBuilder<WorkingBlock> builder)
    {
        builder.ToTable("working_hours");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.HasIndex(b => new { b.ProfessionalId, b.DayOfWeek });
        // FKs, CHECKs and the exclusion constraint against overlapping blocks are added in the migration with raw SQL.
    }
}

internal sealed class ScheduleExceptionConfiguration : IEntityTypeConfiguration<ScheduleException>
{
    public void Configure(EntityTypeBuilder<ScheduleException> builder)
    {
        builder.ToTable("schedule_exceptions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Date).HasColumnType("date");
        builder.Property(e => e.Reason).HasMaxLength(ScheduleException.ReasonMaxLength);
        builder.HasIndex(e => new { e.BusinessId, e.Date });
        // The unique key (business, professional or whole business, day) uses an expression: created in the migration.
    }
}
