namespace Wintech.Hrms.Api.Domain.Profile;

/// <summary>
/// Per-user notification channel opt-in/out (FR-EMP-121). Categories flagged
/// <see cref="Mandatory"/> (security/operational messages) can never be disabled by the user.
/// </summary>
public class NotificationPreference
{
    public Guid Id { get; set; }
    /// <summary>UserAccount.Id the preference row belongs to.</summary>
    public Guid UserId { get; set; }
    /// <summary>Notification category, e.g. Leave, Payroll, Security (canonical catalogue in the Profile module).</summary>
    public string Category { get; set; } = string.Empty;
    public bool InApp { get; set; } = true;
    public bool Push { get; set; } = true;
    public bool Email { get; set; }
    /// <summary>True when the category carries mandatory operational/security messages (cannot be disabled).</summary>
    public bool Mandatory { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Canonical notification categories (FR-EMP-121). Security/System are mandatory.</summary>
public static class NotificationCategories
{
    public const string Security = "Security";
    public const string System = "System";
    public const string Leave = "Leave";
    public const string Attendance = "Attendance";
    public const string Approvals = "Approvals";
    public const string Payroll = "Payroll";
    public const string Announcements = "Announcements";
    public const string Helpdesk = "Helpdesk";
    public const string Documents = "Documents";
    public const string Training = "Training";
    public const string Performance = "Performance";

    /// <summary>Default channel configuration: in-app + push on, e-mail off; mandatory categories fully on.</summary>
    public static readonly IReadOnlyList<(string Category, bool Mandatory)> Catalog =
    [
        (Security, true),
        (System, true),
        (Leave, false),
        (Attendance, false),
        (Approvals, false),
        (Payroll, false),
        (Announcements, false),
        (Helpdesk, false),
        (Documents, false),
        (Training, false),
        (Performance, false)
    ];

    public static bool IsKnown(string category) =>
        Catalog.Any(c => string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase));

    public static bool IsMandatory(string category) =>
        Catalog.Any(c => c.Mandatory && string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase));

    /// <summary>Canonical casing for a known category; returns the input when unknown.</summary>
    public static string Normalize(string category) =>
        Catalog.Select(c => c.Category)
            .FirstOrDefault(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)) ?? category;
}
