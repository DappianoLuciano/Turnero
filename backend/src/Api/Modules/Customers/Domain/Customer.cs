using System.Text.RegularExpressions;
using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.Customers.Domain;

/// <summary>A customer of the business. Tenant data: RLS by business_id. Archived instead of deleted.</summary>
public sealed partial class Customer : ITenantEntity
{
    public const int NameMaxLength = 120;
    public const int EmailMaxLength = 254;
    public const int NotesMaxLength = 1000;

    private Customer()
    {
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Normalized (digits and optional leading +). Unique among non-archived customers of the business.</summary>
    public string? Phone { get; private set; }

    public string? Email { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Archived customers stay in the history of their appointments but are hidden by default.</summary>
    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Strips spaces, dashes, dots and parentheses; returns null for blank input.</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        return PhoneSeparators().Replace(phone, string.Empty);
    }

    public static bool IsValidPhone(string? phone) =>
        NormalizePhone(phone) is not { } p || ValidPhone().IsMatch(p);

    public static Customer Create(Guid businessId, string name, string? phone, string? email, string? notes,
        DateTimeOffset now)
    {
        var customer = new Customer
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            CreatedAt = BusinessTimeZone.EnsureUtc(now),
        };
        customer.Apply(name, phone, email, notes, now);
        return customer;
    }

    public void Update(string name, string? phone, string? email, string? notes, bool isArchived, DateTimeOffset now)
    {
        Apply(name, phone, email, notes, now);
        IsArchived = isArchived;
    }

    private void Apply(string name, string? phone, string? email, string? notes, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Phone = NormalizePhone(phone);
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = BusinessTimeZone.EnsureUtc(now);
    }

    [GeneratedRegex(@"[\s\-\.\(\)]")]
    private static partial Regex PhoneSeparators();

    [GeneratedRegex(@"^\+?[0-9]{6,15}$")]
    private static partial Regex ValidPhone();
}
