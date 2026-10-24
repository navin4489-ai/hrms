namespace Wintech.Hrms.Api.Security;

/// <summary>
/// Canonical permission catalogue (BRD section 3.1). Stored as codes in RolePermission rows;
/// the SuperAdmin role holds "*" which satisfies every check.
/// </summary>
public static class Permissions
{
    public const string EmployeeView = "employee.view";
    public const string EmployeeCreate = "employee.create";
    public const string EmployeeEdit = "employee.edit";
    public const string EmployeeManage = "employee.manage";
    public const string EmployeeExport = "employee.export";

    public const string AttendanceViewTeam = "attendance.view.team";
    public const string AttendanceManage = "attendance.manage";

    public const string LeaveApprove = "leave.approve";
    public const string LeaveManage = "leave.manage";

    public const string PayrollManage = "payroll.manage";
    public const string PayrollPublish = "payroll.publish";
    public const string PayslipViewAny = "payslip.view.any";

    public const string DocumentManage = "document.manage";
    public const string DocumentSensitive = "document.sensitive";

    public const string ExpenseApprove = "expense.approve";
    public const string ExpenseFinance = "expense.finance";

    public const string AssetManage = "asset.manage";

    public const string CaseManage = "case.manage";
    public const string CaseViewRestricted = "case.view.restricted";

    public const string AnnouncementCreate = "announcement.create";
    public const string PerformanceManage = "performance.manage";
    public const string TrainingManage = "training.manage";

    public const string AdminUsers = "admin.users";
    public const string AdminRoles = "admin.roles";
    public const string AdminConfig = "admin.config";

    public const string AuditView = "audit.view";
    public const string ReportView = "report.view";
    public const string ReportExport = "report.export";

    public const string All = "*";

    public static readonly IReadOnlyList<string> AllCodes =
    [
        EmployeeView, EmployeeCreate, EmployeeEdit, EmployeeManage, EmployeeExport,
        AttendanceViewTeam, AttendanceManage,
        LeaveApprove, LeaveManage,
        PayrollManage, PayrollPublish, PayslipViewAny,
        DocumentManage, DocumentSensitive,
        ExpenseApprove, ExpenseFinance,
        AssetManage,
        CaseManage, CaseViewRestricted,
        AnnouncementCreate, PerformanceManage, TrainingManage,
        AdminUsers, AdminRoles, AdminConfig,
        AuditView, ReportView, ReportExport
    ];

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [EmployeeView] = "View employee records",
        [EmployeeCreate] = "Create employees",
        [EmployeeEdit] = "Edit employees",
        [EmployeeManage] = "Manage employee lifecycle (suspend/terminate)",
        [EmployeeExport] = "Export employee data",
        [AttendanceViewTeam] = "View team attendance",
        [AttendanceManage] = "Manage attendance records",
        [LeaveApprove] = "Approve leave and absence requests",
        [LeaveManage] = "Configure leave policies and balances",
        [PayrollManage] = "Manage payroll periods and inputs",
        [PayrollPublish] = "Publish payroll output and payslips",
        [PayslipViewAny] = "View other employees' payslips (highly restricted)",
        [DocumentManage] = "Manage employee documents",
        [DocumentSensitive] = "Access sensitive identity/medical documents",
        [ExpenseApprove] = "Approve expense claims",
        [ExpenseFinance] = "Finance disposition of reimbursements",
        [AssetManage] = "Manage IT asset inventory and assignment",
        [CaseManage] = "Manage HR helpdesk cases and queues",
        [CaseViewRestricted] = "View confidential/restricted HR cases",
        [AnnouncementCreate] = "Create and publish announcements",
        [PerformanceManage] = "Manage performance cycles and reviews",
        [TrainingManage] = "Manage training catalogue and completions",
        [AdminUsers] = "Manage user accounts and access",
        [AdminRoles] = "Manage roles and permissions",
        [AdminConfig] = "Manage system configuration and operations",
        [AuditView] = "Read audit log (read-only)",
        [ReportView] = "Run reports",
        [ReportExport] = "Export reports"
    };
}

/// <summary>Role catalogue (BRD section 3).</summary>
public static class Roles
{
    public const string Employee = "Employee";
    public const string LineManager = "LineManager";
    public const string HRExecutive = "HRExecutive";
    public const string HRHead = "HRHead";
    public const string PayrollFinance = "PayrollFinance";
    public const string ITAssetAdmin = "ITAssetAdmin";
    public const string SystemAdmin = "SystemAdmin";
    public const string Auditor = "Auditor";
    public const string SuperAdmin = "SuperAdmin";

    public static readonly IReadOnlyList<string> All =
    [
        Employee, LineManager, HRExecutive, HRHead, PayrollFinance,
        ITAssetAdmin, SystemAdmin, Auditor, SuperAdmin
    ];

    public static readonly IReadOnlyDictionary<string, (string Description, string[] Permissions)> Definitions =
        new Dictionary<string, (string, string[])>
        {
            [Employee] = ("Own records, self-service requests and HR support", []),
            [LineManager] = ("Approves direct reports' leave, attendance corrections, expenses and goals",
            [
                Permissions.AttendanceViewTeam, Permissions.LeaveApprove, Permissions.ExpenseApprove,
                Permissions.ReportView
            ]),
            [HRExecutive] = ("Maintains employee records, onboarding, documents and HR cases",
            [
                Permissions.EmployeeView, Permissions.EmployeeCreate, Permissions.EmployeeEdit,
                Permissions.AttendanceViewTeam, Permissions.AttendanceManage, Permissions.LeaveApprove,
                Permissions.LeaveManage, Permissions.DocumentManage, Permissions.CaseManage,
                Permissions.AnnouncementCreate, Permissions.TrainingManage, Permissions.ReportView
            ]),
            [HRHead] = ("HR-wide access, exceptions and dashboards",
            [
                Permissions.EmployeeView, Permissions.EmployeeCreate, Permissions.EmployeeEdit,
                Permissions.EmployeeManage, Permissions.EmployeeExport, Permissions.AttendanceViewTeam,
                Permissions.AttendanceManage, Permissions.LeaveApprove, Permissions.LeaveManage,
                Permissions.DocumentManage, Permissions.DocumentSensitive, Permissions.CaseManage,
                Permissions.CaseViewRestricted, Permissions.AnnouncementCreate, Permissions.PerformanceManage,
                Permissions.TrainingManage, Permissions.ReportView, Permissions.ReportExport
            ]),
            [PayrollFinance] = ("Payroll periods, inputs, reimbursement finance and payslip publishing",
            [
                Permissions.PayrollManage, Permissions.PayrollPublish, Permissions.PayslipViewAny,
                Permissions.ExpenseApprove, Permissions.ExpenseFinance, Permissions.ReportView,
                Permissions.ReportExport, Permissions.EmployeeView
            ]),
            [ITAssetAdmin] = ("Asset inventory, allocation, return and IT service tasks",
            [
                Permissions.AssetManage, Permissions.CaseManage, Permissions.EmployeeView
            ]),
            [SystemAdmin] = ("Privileged configuration of users, roles, integrations and settings",
            [
                Permissions.AdminUsers, Permissions.AdminRoles, Permissions.AdminConfig,
                Permissions.EmployeeView, Permissions.ReportView
            ]),
            [Auditor] = ("Read-only audit log and approved reports",
            [
                Permissions.AuditView, Permissions.ReportView
            ]),
            [SuperAdmin] = ("Tenant owner: everything, monitored and MFA protected", [Permissions.All])
        };

    /// <summary>Roles that receive MFA when <c>Security:RequireMfaForAdmins</c> is enabled.</summary>
    public static readonly string[] AdminRoles = [SuperAdmin, SystemAdmin, HRHead, PayrollFinance];
}
