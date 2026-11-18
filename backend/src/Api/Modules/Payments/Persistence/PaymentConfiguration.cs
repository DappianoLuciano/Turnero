using Api.Modules.Payments.Domain;
using Api.Shared.Monetary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Payments.Persistence;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Amount).HasPrecision(Money.Precision, Money.Scale);
        builder.Property(p => p.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Reason).HasMaxLength(Payment.ReasonMaxLength);
        builder.Ignore(p => p.IsReversal);
        builder.HasIndex(p => new { p.AppointmentId, p.CreatedAt });
        builder.HasIndex(p => new { p.BusinessId, p.CreatedAt });
        // A payment can be reversed only once.
        builder.HasIndex(p => p.ReversesId).IsUnique().HasFilter("reverses_id IS NOT NULL");
        // FKs, CHECKs and the revoked UPDATE/DELETE (append-only) are added in the migration with raw SQL.
    }
}
