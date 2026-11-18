using Api.Modules.CashRegister.Domain;
using Api.Shared.Monetary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.CashRegister.Persistence;

internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("cash_movements");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Amount).HasPrecision(Money.Precision, Money.Scale);
        builder.Property(m => m.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(m => m.Concept).HasMaxLength(CashMovement.ConceptMaxLength).IsRequired();
        builder.Property(m => m.Reason).HasMaxLength(CashMovement.ReasonMaxLength);
        builder.Ignore(m => m.IsReversal);
        builder.HasIndex(m => new { m.BusinessId, m.CreatedAt });
        builder.HasIndex(m => m.ReversesId).IsUnique().HasFilter("reverses_id IS NOT NULL");
    }
}

internal sealed class CashClosingConfiguration : IEntityTypeConfiguration<CashClosing>
{
    public void Configure(EntityTypeBuilder<CashClosing> builder)
    {
        builder.ToTable("cash_closings");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Date).HasColumnType("date");
        builder.Property(c => c.Expected).HasPrecision(Money.Precision, Money.Scale);
        builder.Property(c => c.Counted).HasPrecision(Money.Precision, Money.Scale);
        builder.Property(c => c.Difference).HasPrecision(Money.Precision, Money.Scale);
        builder.Property(c => c.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(c => c.Note).HasMaxLength(CashClosing.NoteMaxLength);
        // One closing per business and day.
        builder.HasIndex(c => new { c.BusinessId, c.Date }).IsUnique();
    }
}
