namespace Wintech.Hrms.Api.Common.Tenancy;

/// <summary>
/// Catalogue of modules a SuperAdmin can enable/disable per organization (FR-ADM-002).
/// The key is stored in <c>OrgModuleSetting.ModuleKey</c>; the label is shown in admin UIs.
/// </summary>
public static class OrgModules
{
    public const string Attendance = "attendance";
    public const string Leave = "leave";
    public const string Payroll = "payroll";
    public const string Documents = "documents";
    public const string Assets = "assets";
    public const string Expenses = "expenses";
    public const string Performance = "performance";
    public const string Training = "training";
    public const string Helpdesk = "helpdesk";
    public const string Announcements = "announcements";
    public const string Chat = "chat";
    public const string Calling = "calling";

    /// <summary>Default enablement for a freshly created organization.</summary>
    public static readonly IReadOnlyDictionary<string, bool> Defaults = new Dictionary<string, bool>
    {
        [Attendance] = true,
        [Leave] = true,
        [Payroll] = true,
        [Documents] = true,
        [Assets] = true,
        [Expenses] = true,
        [Performance] = true,
        [Training] = true,
        [Helpdesk] = true,
        [Announcements] = true,
        [Chat] = false,
        [Calling] = false,
    };

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Attendance] = "Attendance & Shifts",
        [Leave] = "Leave Management",
        [Payroll] = "Payroll & Payslips",
        [Documents] = "Documents & Letters",
        [Assets] = "Asset Inventory",
        [Expenses] = "Expenses & Travel",
        [Performance] = "Performance Reviews",
        [Training] = "Training & Learning",
        [Helpdesk] = "HR Helpdesk",
        [Announcements] = "Announcements",
        [Chat] = "Employee Chat",
        [Calling] = "Voice & Video Calling",
    };

    public static readonly IReadOnlyList<string> AllKeys = Defaults.Keys.ToArray();

    public static bool IsKnown(string key) => Defaults.ContainsKey(key);

    public static bool DefaultFor(string key) => Defaults.TryGetValue(key, out var v) && v;
}
