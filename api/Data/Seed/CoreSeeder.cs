using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Domain.Organization;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Idempotent foundation seed: RBAC roles/permissions, organisation structure, the employee master,
/// demo login accounts and the approval workflow definitions every module relies on.
/// Feature modules add their own <see cref="IDataSeeder"/> implementations with Order >= 100.
/// </summary>
public sealed class CoreSeeder : IDataSeeder
{
    public const string DemoPassword = "Passw0rd!";

    private readonly AppDbContext _db;
    private readonly ILogger<CoreSeeder> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public CoreSeeder(AppDbContext db, ILogger<CoreSeeder> logger, IConfiguration configuration, IHostEnvironment environment)
    {
        _db = db;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
    }

    public int Order => 0;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRolesAndPermissionsAsync(cancellationToken);
        if (await _db.Organizations.AsNoTracking().AnyAsync(cancellationToken))
        {
            _logger.LogDebug("Core data already seeded; roles/permissions verified.");
            return;
        }

        var organization = new Organization
        {
            Id = Id(1),
            Name = "Wintech Solutions Pvt Ltd",
            Code = "WINTECH",
            Country = "IN",
            TimeZone = "Asia/Kolkata",
            Currency = "INR",
            Status = ReferenceStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _db.Organizations.Add(organization);

        // ---- reference data -------------------------------------------------
        var legalEntity = new OrgUnit { Id = Id(10), Code = "WSP", Name = organization.Name, Type = OrgUnitType.LegalEntity };
        var departments = new List<(string Code, string Name, Guid Id)>
        {
            ("OPS", "Operations", Id(11)), ("ENG", "Engineering", Id(12)), ("HR", "Human Resources", Id(13)),
            ("FIN", "Finance", Id(14)), ("SAL", "Sales", Id(15))
        };
        _db.OrgUnits.Add(legalEntity);
        _db.OrgUnits.AddRange(departments.Select(d => new OrgUnit
        {
            Id = d.Id, Code = d.Code, Name = d.Name, Type = OrgUnitType.Department,
            ParentId = legalEntity.Id, Status = ReferenceStatus.Active
        }));

        var locations = new[]
        {
            new Location { Id = Id(20), Code = "BLR", Name = "Bengaluru HQ", City = "Bengaluru", Country = "IN", TimeZone = "Asia/Kolkata" },
            new Location { Id = Id(21), Code = "MUM", Name = "Mumbai Office", City = "Mumbai", Country = "IN", TimeZone = "Asia/Kolkata" },
            new Location { Id = Id(22), Code = "REM", Name = "Remote", City = "Distributed", Country = "IN", TimeZone = "Asia/Kolkata" }
        };
        _db.Locations.AddRange(locations);

        var designations = new[]
        {
            new Designation { Id = Id(30), Code = "SE", Name = "Software Engineer", Grade = "P2" },
            new Designation { Id = Id(31), Code = "SRSE", Name = "Senior Software Engineer", Grade = "P3" },
            new Designation { Id = Id(32), Code = "HREX", Name = "HR Executive", Grade = "P2" },
            new Designation { Id = Id(33), Code = "ACC", Name = "Accountant", Grade = "P2" },
            new Designation { Id = Id(34), Code = "SALEX", Name = "Sales Executive", Grade = "P2" },
            new Designation { Id = Id(35), Code = "MGR", Name = "Manager", Grade = "M1" }
        };
        _db.Designations.AddRange(designations);

        // ---- employees ------------------------------------------------------
        Guid Dept(string code) => departments.First(d => d.Code == code).Id;
        Guid Loc(string code) => locations.First(l => l.Code == code).Id;
        Guid Desig(string code) => designations.First(d => d.Code == code).Id;

        var employees = new List<Employee>();
        Employee Add(string number, string first, string last, string desigCode, string deptCode, string locCode,
            Guid? managerId, DateOnly joinDate, EmploymentStatus status = EmploymentStatus.Active,
            EmploymentType type = EmploymentType.FullTime, DateOnly? probationEnd = null)
        {
            var employee = new Employee
            {
                Id = Id(100 + employees.Count),
                EmployeeNumber = number,
                OrganizationId = organization.Id,
                FirstName = first,
                LastName = last,
                DisplayName = $"{first} {last}",
                WorkEmail = $"{first}.{last}".ToLowerInvariant() + "@wintech.example",
                Phone = $"+91-98{employees.Count:D8}",
                JoinDate = joinDate,
                ProbationEndDate = probationEnd,
                EmploymentStatus = status,
                StatusEffectiveDate = joinDate,
                EmploymentType = type,
                DesignationId = Desig(desigCode),
                DepartmentId = Dept(deptCode),
                LocationId = Loc(locCode),
                ManagerId = managerId,
                CostCentre = $"{deptCode}-01"
            };
            employees.Add(employee);
            return employee;
        }

        var emp001 = Add("EMP001", "Ravi", "Sharma", "MGR", "OPS", "BLR", null, new DateOnly(2018, 4, 2));
        var emp002 = Add("EMP002", "Anil", "Kumar", "MGR", "ENG", "BLR", emp001.Id, new DateOnly(2019, 1, 14));
        var emp003 = Add("EMP003", "Meera", "Iyer", "MGR", "HR", "BLR", emp001.Id, new DateOnly(2018, 8, 20));
        var emp004 = Add("EMP004", "Sandeep", "Rao", "HREX", "HR", "BLR", emp003.Id, new DateOnly(2021, 3, 1));
        var emp005 = Add("EMP005", "Kavita", "Menon", "ACC", "FIN", "MUM", emp001.Id, new DateOnly(2020, 6, 15));
        var emp006 = Add("EMP006", "Priya", "Nair", "SE", "ENG", "BLR", emp002.Id, new DateOnly(2022, 6, 13));
        var emp007 = Add("EMP007", "Rohit", "Verma", "SE", "ENG", "MUM", emp002.Id, new DateOnly(2023, 2, 6));
        var emp008 = Add("EMP008", "Joseph", "Mathew", "ACC", "FIN", "BLR", emp001.Id, new DateOnly(2019, 9, 2));
        var emp009 = Add("EMP009", "Vikram", "Shah", "SALEX", "SAL", "MUM", emp001.Id, new DateOnly(2021, 11, 8));
        var emp010 = Add("EMP010", "Ananya", "Deshpande", "SRSE", "ENG", "BLR", emp002.Id, new DateOnly(2020, 1, 20));
        var emp011 = Add("EMP011", "Farhan", "Khan", "SALEX", "SAL", "REM", emp001.Id, new DateOnly(2024, 7, 1));
        var emp012 = Add("EMP012", "Deepa", "Krishnan", "ACC", "FIN", "BLR", emp005.Id,
            new DateOnly(2026, 8, 17), EmploymentStatus.Probation, EmploymentType.FullTime, new DateOnly(2027, 2, 16));

        _db.Employees.AddRange(employees);

        foreach (var employee in employees)
        {
            _db.EmployeePersonalDetails.Add(new EmployeePersonalDetail
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                PreferredName = employee.FirstName,
                PersonalEmail = employee.WorkEmail.Replace("@wintech.example", "@mail.example"),
                PersonalPhone = employee.Phone,
                DateOfBirth = employee.JoinDate.AddYears(-26),
                Gender = Gender.PreferNotToSay,
                Address = $"No. {employees.IndexOf(employee) + 1}, 4th Cross, Indiranagar",
                City = "Bengaluru",
                PostalCode = "560038",
                EmergencyContactName = $"{employee.LastName} Family",
                EmergencyContactPhone = "+91-9900000000",
                UpdatedAt = DateTime.UtcNow
            });

            _db.EmploymentHistories.Add(new EmploymentHistory
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                EventType = HistoryEventType.Joining,
                EffectiveDate = employee.JoinDate,
                NewValue = "Joined as " + designations.First(d => d.Id == employee.DesignationId).Name,
                ApprovedBy = "HR",
                CreatedAt = employee.JoinDate.ToDateTime(TimeOnly.MinValue)
            });
        }

        // Highly restricted data for the primary demo user (masked in every response).
        _db.EmployeeSensitiveDataSet.Add(new EmployeeSensitiveData
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp006.Id,
            BankAccountNumber = "001122334455",
            BankName = "HDFC Bank",
            BankBranch = "Indiranagar, Bengaluru",
            PaymentMethod = "BankTransfer",
            NationalIdType = "Aadhaar",
            NationalIdNumber = "4591 2234 6789",
            TaxId = "PAN ABCPN1234K",
            BankVerifiedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // ---- user accounts --------------------------------------------------
        var seedPassword = _configuration["Security:SeedDemoPassword"];
        if (string.IsNullOrWhiteSpace(seedPassword))
        {
            if (!_environment.IsDevelopment())
                throw new InvalidOperationException("Security:SeedDemoPassword must be configured before seeding a non-development database.");
            seedPassword = DemoPassword;
        }

        var passwordHash = PasswordHasher.Hash(seedPassword);
        var accounts = new (string UserName, Employee Employee, string[] Roles)[]
        {
            ("admin", emp001, [Roles.SuperAdmin]),
            ("manager", emp002, [Roles.LineManager]),
            ("hr", emp003, [Roles.HRHead, Roles.HRExecutive]),
            ("hrexec", emp004, [Roles.HRExecutive]),
            ("payroll", emp005, [Roles.PayrollFinance]),
            ("employee", emp006, [Roles.Employee]),
            ("employee2", emp007, [Roles.Employee]),
            ("auditor", emp008, [Roles.Auditor])
        };

        foreach (var (userName, employee, roleNames) in accounts)
        {
            var account = new UserAccount
            {
                Id = Guid.NewGuid(),
                UserName = userName.ToLowerInvariant(),
                Email = employee.WorkEmail,
                PasswordHash = passwordHash,
                EmployeeId = employee.Id,
                Status = UserStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            foreach (var roleName in roleNames)
            {
                var role = await EnsureRoleAsync(roleName, cancellationToken);
                account.UserRoles.Add(new UserRole { UserId = account.Id, RoleId = role.Id });
            }

            _db.UserAccounts.Add(account);
        }

        await SeedWorkflowsAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded core HRMS data: {Employees} employees, {Accounts} user accounts, {Departments} departments.",
            employees.Count, accounts.Length, departments.Count);
    }

    private async Task EnsureRolesAndPermissionsAsync(CancellationToken cancellationToken)
    {
        foreach (var roleName in Roles.All) await EnsureRoleAsync(roleName, cancellationToken);
    }

    private async Task<Role> EnsureRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var role = await _db.Roles.Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken);

        if (role is null)
        {
            var definition = Roles.Definitions[roleName];
            role = new Role
            {
                Id = Guid.NewGuid(),
                Name = roleName,
                Description = definition.Description,
                IsSystem = true
            };
            foreach (var permission in definition.Permissions)
                role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionCode = permission });
            _db.Roles.Add(role);
            await _db.SaveChangesAsync(cancellationToken);
            return role;
        }

        var known = Roles.Definitions[roleName].Permissions;
        var existing = role.RolePermissions.Select(p => p.PermissionCode).ToHashSet(StringComparer.Ordinal);
        var missing = known.Where(p => !existing.Contains(p)).ToList();
        if (missing.Count > 0)
        {
            foreach (var permission in missing)
                _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionCode = permission });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return role;
    }

    private async Task SeedWorkflowsAsync(CancellationToken cancellationToken)
    {
        void Define(string requestType, string code, string name, params (int Step, string Name, ApproverType Type, string? Value, int? SlaDays)[] steps)
        {
            if (_db.ApprovalWorkflowDefinitions.Any(w => w.RequestType == requestType && w.IsActive)) return;

            var workflow = new ApprovalWorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                RequestType = requestType,
                Version = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            foreach (var (step, stepName, type, value, slaDays) in steps)
            {
                workflow.Steps.Add(new ApprovalWorkflowStep
                {
                    Id = Guid.NewGuid(),
                    StepNumber = step,
                    Name = stepName,
                    ApproverType = type,
                    ApproverValue = value,
                    SLADays = slaDays
                });
            }
            _db.ApprovalWorkflowDefinitions.Add(workflow);
        }

        Define("Leave", "WF-LEAVE", "Leave request approval",
            (1, "Line manager", ApproverType.Manager, null, 2));
        Define("AttendanceCorrection", "WF-ATT-CORR", "Attendance correction approval",
            (1, "Line manager", ApproverType.Manager, null, 2));
        Define("ShiftSwap", "WF-SHIFT-SWAP", "Shift swap approval",
            (1, "Line manager", ApproverType.Manager, null, 1));
        Define("Expense", "WF-EXPENSE", "Expense claim approval",
            (1, "Line manager", ApproverType.Manager, null, 3),
            (2, "Finance review", ApproverType.Role, Roles.PayrollFinance, 3));
        Define("Travel", "WF-TRAVEL", "Travel request approval",
            (1, "Line manager", ApproverType.Manager, null, 3),
            (2, "Finance review", ApproverType.Role, Roles.PayrollFinance, 3));
        Define("Letter", "WF-LETTER", "Letter issuance approval",
            (1, "HR review", ApproverType.Role, Roles.HRExecutive, 3));
        Define("ProfileChange", "WF-PROFILE", "Sensitive profile change verification",
            (1, "HR verification", ApproverType.Role, Roles.HRExecutive, 3));
        Define("Resignation", "WF-RESIGN", "Resignation approval",
            (1, "Line manager", ApproverType.Manager, null, 3),
            (2, "HR confirmation", ApproverType.Role, Roles.HRHead, 5));

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static Guid Id(int number) => Guid.Parse($"00000000-0000-0000-0000-{number:D12}");
}
