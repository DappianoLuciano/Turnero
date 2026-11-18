using Api.Modules.Customers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Customers.Persistence;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(Customer.EmailMaxLength);
        builder.Property(c => c.Notes).HasMaxLength(Customer.NotesMaxLength);
        builder.HasIndex(c => new { c.BusinessId, c.Name });
        // One active customer per phone and business. Archived ones free their phone.
        builder.HasIndex(c => new { c.BusinessId, c.Phone })
            .IsUnique()
            .HasFilter("phone IS NOT NULL AND NOT is_archived");
        // FK to businesses is added in the migration with raw SQL: modules do not map other modules' entities.
    }
}
