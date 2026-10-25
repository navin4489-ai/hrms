using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Tenancy;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Domain.Organization;
using Wintech.Hrms.Api.Modules.Profile;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Employees;

/// <summary>
/// Employee master data lifecycle (contract §6, FR-ADM-020..027):
/// privacy-aware directory, permission-gated detail/history/admin list, creation (BR-001/FR-ADM-021),
/// effective-dated edits with employment-history rows (FR-ADM-026) and status changes that revoke
/// access immediately (FR-ADM-024, BR-008).
/// </summary>
public sealed class EmployeesService : IEmployeesService
{
    private const string AvatarBase = "/api/v1/employees/";

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<EmployeesService> _logger;

    public EmployeesService(AppDbContext db, ICurrentUser currentUser, ITenantContext tenant, IClock clock,
        IAuditService audit, ILogger<EmployeesService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _tenant = tenant;
        _clock = clock;
        _audit = audit;
        _logger = logger;
    }

    // ---------------------------------------------------------------- GET /employees/directory (FR-EMP-024)

    public async Task<PagedResult<DirectoryItemDto>> GetDirectoryAsync(string? q, Guid? departmentId,
        Guid? locationId, string? sort, PageRequest page, CancellationToken cancellationToken = default)
    {
        IQueryable<Employee> query = _db.Employees.AsNoTracking()
            .Where(e => e.EmploymentStatus == EmploymentStatus.Active ||
                        e.EmploymentStatus == EmploymentStatus.Probation);

        if (departmentId.HasValue) query = query.Where(e => e.DepartmentId == departmentId.Value);
        if (locationId.HasValue) query = query.Where(e => e.LocationId == locationId.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(e =>
                e.DisplayName.ToLower().Contains(term) ||
                e.EmployeeNumber.ToLower().Contains(term) ||
                e.WorkEmail.ToLower().Contains(term));
        }

        var sorted = ApplySort(query, sort);

        return await sorted
            .Select(e => new
            {
                e.Id,
                e.EmployeeNumber,
                e.DisplayName,
                Designation = e.Designation != null ? e.Designation.Name : null,
                Department = e.Department != null ? e.Department.Name : null,
                Location = e.Location != null ? e.Location.Name : null,
                e.WorkEmail,
                HasPhone = e.Phone != null,
                HasPhoto = e.PhotoStorageKey != null
            })
            .ToPagedAsync(page,
                row => new DirectoryItemDto
                {
                    EmployeeId = row.Id,
                    EmployeeNumber = row.EmployeeNumber,
                    DisplayName = row.DisplayName,
                    Designation = row.Designation,
                    Department = row.Department,
                    Location = row.Location,
                    WorkEmail = row.WorkEmail,
                    HasPhone = row.HasPhone,
                    AvatarUrl = row.HasPhoto ? $"{AvatarBase}{row.Id}/avatar" : null
                },
                cancellationToken);
    }

    // ---------------------------------------------------------------- GET /employees/{id}

    public async Task<EmployeeProfileDto> GetDetailAsync(Guid id, bool unmask, CancellationToken cancellationToken = default)
    {
        var isOwn = _currentUser.EmployeeId == id;
        if (!isOwn && !_currentUser.HasPermission(Permissions.EmployeeView))
            throw new NotFoundException("The requested resource was not found.");

        var row = await LoadDetailRowAsync(id, cancellationToken)
                  ?? throw new NotFoundException("The requested resource was not found.");

        var canSeeSensitive = _currentUser.HasPermission(Permissions.DocumentSensitive);
        var unmasked = canSeeSensitive && unmask;

        var dto = MapDetail(row, includeSensitive: canSeeSensitive, unmask: unmasked);
        dto = dto with { Status = row.EmploymentStatus };

        // BR-009 / FR-ADM-152: every unmasked read of bank/national-ID data is audited.
        if (unmasked && row.Sensitive is not null)
        {
            await _audit.RecordAsync("employee.sensitive_unmask", nameof(Employee), id.ToString(),
                details: new
                {
                    employeeNumber = row.EmployeeNumber,
                    fields = new[] { "BankAccountNumber", "BankName", "NationalIdType", "NationalIdNumber", "TaxId" }
                },
                cancellationToken: cancellationToken);
        }

        return dto;
    }

    // ---------------------------------------------------------------- GET /employees/{id}/history

    public async Task<IReadOnlyList<EmploymentHistoryItemDto>> GetHistoryAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == id, cancellationToken);
        if (!exists) throw new NotFoundException("The requested resource was not found.");

        return await _db.EmploymentHistories.AsNoTracking()
            .Where(h => h.EmployeeId == id)
            .OrderByDescending(h => h.EffectiveDate)
            .ThenByDescending(h => h.CreatedAt)
            .Select(h => new EmploymentHistoryItemDto
            {
                EventType = h.EventType,
                EffectiveDate = h.EffectiveDate,
                OldValue = h.OldValue,
                NewValue = h.NewValue,
                Reason = h.Reason,
                ApprovedBy = h.ApprovedBy
            })
            .ToListAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- GET /employees (admin list)

    public async Task<PagedResult<EmployeeListItem>> GetListAsync(string? q, EmploymentStatus? status,
        Guid? departmentId, Guid? locationId, PageRequest page, CancellationToken cancellationToken = default)
    {
        IQueryable<Employee> query = _db.Employees.AsNoTracking();

        if (status.HasValue) query = query.Where(e => e.EmploymentStatus == status.Value);
        if (departmentId.HasValue) query = query.Where(e => e.DepartmentId == departmentId.Value);
        if (locationId.HasValue) query = query.Where(e => e.LocationId == locationId.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(e =>
                e.DisplayName.ToLower().Contains(term) ||
                e.EmployeeNumber.ToLower().Contains(term) ||
                e.WorkEmail.ToLower().Contains(term));
        }

        return await query
            .OrderBy(e => e.DisplayName)
            .Select(e => new
            {
                e.Id,
                e.EmployeeNumber,
                e.DisplayName,
                e.WorkEmail,
                Designation = e.Designation != null ? e.Designation.Name : null,
                Department = e.Department != null ? e.Department.Name : null,
                Location = e.Location != null ? e.Location.Name : null,
                Status = e.EmploymentStatus,
                ManagerName = e.Manager != null ? e.Manager.DisplayName : null,
                e.JoinDate
            })
            .ToPagedAsync(page,
                row => new EmployeeListItem
                {
                    EmployeeId = row.Id,
                    EmployeeNumber = row.EmployeeNumber,
                    DisplayName = row.DisplayName,
                    WorkEmail = row.WorkEmail,
                    Designation = row.Designation,
                    Department = row.Department,
                    Location = row.Location,
                    Status = row.Status,
                    ManagerName = row.ManagerName,
                    JoiningDate = row.JoinDate
                },
                cancellationToken);
    }

    // ---------------------------------------------------------------- POST /employees (FR-ADM-020/021)

    public async Task<EmployeeProfileDto> CreateAsync(CreateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, List<string>>();

        if (request.JoinDate == default || request.JoinDate < new DateOnly(1900, 1, 1))
            errors["joinDate"] = ["A valid join date is required."];
        if (!Enum.IsDefined(request.EmploymentType))
            errors["employmentType"] = ["Unknown employment type."];

        var designation = await _db.Designations.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DesignationId, cancellationToken);
        if (designation is null) errors["designationId"] = ["Unknown designation."];

        var department = await _db.OrgUnits.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.DepartmentId && u.Type == OrgUnitType.Department, cancellationToken);
        if (department is null) errors["departmentId"] = ["Unknown department."];

        var location = await _db.Locations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.LocationId, cancellationToken);
        if (location is null) errors["locationId"] = ["Unknown location."];

        var legalEntity = await _db.OrgUnits.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.LegalEntityId && u.Type == OrgUnitType.LegalEntity, cancellationToken);
        if (legalEntity is null) errors["legalEntityId"] = ["Unknown legal entity."];

        if (request.ManagerId.HasValue &&
            !await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == request.ManagerId.Value, cancellationToken))
            errors["managerId"] = ["Unknown manager."];

        if (errors.Count > 0) throw Validation(errors);

        var organizationId = _tenant.OrganizationId ?? await _db.Organizations.AsNoTracking()
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(cancellationToken) ?? Guid.Empty;
        if (organizationId == Guid.Empty)
            throw new NotFoundException("No organization is in scope. SuperAdmins must select a target organization.");

        var employeeNumber = string.IsNullOrWhiteSpace(request.EmployeeNumber)
            ? await GenerateEmployeeNumberAsync(organizationId, cancellationToken)
            : request.EmployeeNumber.Trim();

        var email = request.WorkEmail.Trim();

        // BR-001 / FR-ADM-021: one canonical number (globally unique), no duplicate email within the org (409).
        if (await _db.Employees.AsNoTracking().IgnoreQueryFilters()
                .AnyAsync(e => e.EmployeeNumber.ToLower() == employeeNumber.ToLower(), cancellationToken))
            throw new ConflictException($"Employee number '{employeeNumber}' already exists.");
        if (await _db.Employees.AsNoTracking().IgnoreQueryFilters()
                .AnyAsync(e => e.OrganizationId == organizationId && e.WorkEmail.ToLower() == email.ToLower(), cancellationToken))
            throw new ConflictException($"An employee with work email '{email}' already exists.");

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeNumber = employeeNumber,
            OrganizationId = organizationId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            WorkEmail = email,
            Phone = NullIfWhiteSpace(request.Phone),
            JoinDate = request.JoinDate,
            EmploymentType = request.EmploymentType,
            EmploymentStatus = EmploymentStatus.Active,
            StatusEffectiveDate = request.JoinDate,
            DesignationId = request.DesignationId,
            DepartmentId = request.DepartmentId,
            LocationId = request.LocationId,
            ManagerId = request.ManagerId
        };
        _db.Employees.Add(employee);

        _db.EmploymentHistories.Add(new EmploymentHistory
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            EventType = HistoryEventType.Joining,
            EffectiveDate = request.JoinDate,
            NewValue = $"Joined as {designation!.Name}",
            ApprovedBy = _currentUser.DisplayName,
            CreatedAt = _clock.UtcNow
        });

        _audit.Add("employee.create", nameof(Employee), employee.Id.ToString(),
            details: new
            {
                employee.EmployeeNumber,
                workEmail = employee.WorkEmail,
                designation = designation.Name,
                department = department!.Name,
                location = location!.Name,
                joinDate = employee.JoinDate
            });

        await SaveOrConflictAsync(cancellationToken);

        _logger.LogInformation("Employee {Number} created by {Actor}.", employee.EmployeeNumber, _currentUser.EmployeeNumber);
        return await RequireDetailAsync(employee.Id, cancellationToken);
    }

    // ---------------------------------------------------------------- PUT /employees/{id} (FR-ADM-026)

    public async Task<EmployeeProfileDto> UpdateAsync(Guid id, UpdateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                       ?? throw new NotFoundException("The requested resource was not found.");

        var effectiveDate = request.EffectiveDate ?? _clock.Today;
        var approvedBy = _currentUser.DisplayName;
        var history = new List<EmploymentHistory>();
        var changed = new List<string>();

        void Record(HistoryEventType eventType, string oldValue, string newValue)
        {
            history.Add(new EmploymentHistory
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                EventType = eventType,
                EffectiveDate = effectiveDate,
                OldValue = Truncate(oldValue),
                NewValue = Truncate(newValue),
                Reason = request.Reason,
                ApprovedBy = approvedBy,
                CreatedAt = _clock.UtcNow
            });
        }

        if (!string.IsNullOrWhiteSpace(request.FirstName))
            employee.FirstName = request.FirstName.Trim();
        if (!string.IsNullOrWhiteSpace(request.LastName))
            employee.LastName = request.LastName.Trim();

        if (!string.IsNullOrWhiteSpace(request.WorkEmail))
        {
            var email = request.WorkEmail.Trim();
            if (!string.Equals(employee.WorkEmail, email, StringComparison.OrdinalIgnoreCase))
            {
                if (await _db.Employees.AsNoTracking()
                        .AnyAsync(e => e.Id != employee.Id && e.WorkEmail.ToLower() == email.ToLower(), cancellationToken))
                    throw new ConflictException($"An employee with work email '{email}' already exists.");
                employee.WorkEmail = email;
                changed.Add("workEmail");
            }
        }

        if (request.ClearPhone && employee.Phone is not null)
        {
            employee.Phone = null;
            changed.Add("phone");
        }
        else if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            employee.Phone = request.Phone.Trim();
            changed.Add("phone");
        }

        // Payroll/reporting-relevant fields keep effective-dated history (FR-ADM-026, BR-002).
        if (request.DesignationId.HasValue && request.DesignationId.Value != employee.DesignationId)
        {
            var names = await DesignationNamesAsync([employee.DesignationId, request.DesignationId.Value], cancellationToken);
            var oldName = names.GetValueOrDefault(employee.DesignationId, "(unknown)");
            var newName = names.GetValueOrDefault(request.DesignationId.Value, "(unknown)");
            employee.DesignationId = request.DesignationId.Value;
            Record(HistoryEventType.DesignationChange, oldName, newName);
            changed.Add("designation");
        }

        if (request.DepartmentId.HasValue && request.DepartmentId.Value != employee.DepartmentId)
        {
            var names = await OrgUnitNamesAsync([employee.DepartmentId, request.DepartmentId.Value], cancellationToken);
            var oldName = names.GetValueOrDefault(employee.DepartmentId, "(unknown)");
            var newName = names.GetValueOrDefault(request.DepartmentId.Value, "(unknown)");
            employee.DepartmentId = request.DepartmentId.Value;
            Record(HistoryEventType.DepartmentChange, oldName, newName);
            changed.Add("department");
        }

        if (request.LocationId.HasValue && request.LocationId.Value != employee.LocationId)
        {
            var names = await LocationNamesAsync([employee.LocationId, request.LocationId.Value], cancellationToken);
            var oldName = names.GetValueOrDefault(employee.LocationId, "(unknown)");
            var newName = names.GetValueOrDefault(request.LocationId.Value, "(unknown)");
            employee.LocationId = request.LocationId.Value;
            Record(HistoryEventType.LocationChange, oldName, newName);
            changed.Add("location");
        }

        if (request.ClearManager && employee.ManagerId is { } currentManagerId)
        {
            var names = await EmployeeNamesAsync([currentManagerId], cancellationToken);
            var oldName = names.GetValueOrDefault(currentManagerId, "(unknown)");
            employee.ManagerId = null;
            Record(HistoryEventType.ManagerChange, oldName, "(none)");
            changed.Add("manager");
        }
        else if (request.ManagerId.HasValue && request.ManagerId.Value != employee.ManagerId)
        {
            var managerIds = new List<Guid>();
            if (employee.ManagerId.HasValue) managerIds.Add(employee.ManagerId.Value);
            managerIds.Add(request.ManagerId.Value);
            var names = await EmployeeNamesAsync(managerIds, cancellationToken);
            var oldName = employee.ManagerId.HasValue
                ? names.GetValueOrDefault(employee.ManagerId.Value, "(unknown)")
                : "(none)";
            var newName = names.GetValueOrDefault(request.ManagerId.Value, "(unknown)");
            employee.ManagerId = request.ManagerId.Value;
            Record(HistoryEventType.ManagerChange, oldName, newName);
            changed.Add("manager");
        }

        if (request.EmploymentType.HasValue && request.EmploymentType.Value != employee.EmploymentType)
        {
            Record(HistoryEventType.EmploymentTypeChange, employee.EmploymentType.ToString(),
                request.EmploymentType.Value.ToString());
            employee.EmploymentType = request.EmploymentType.Value;
            changed.Add("employmentType");
        }

        if (request.ClearCostCentre && employee.CostCentre is not null)
        {
            employee.CostCentre = null;
            changed.Add("costCentre");
        }
        else if (!string.IsNullOrWhiteSpace(request.CostCentre))
        {
            employee.CostCentre = request.CostCentre.Trim();
            changed.Add("costCentre");
        }

        if (request.ProbationEndDate.HasValue && request.ProbationEndDate.Value != employee.ProbationEndDate)
        {
            employee.ProbationEndDate = request.ProbationEndDate.Value;
            changed.Add("probationEndDate");
        }

        if (history.Count > 0) _db.EmploymentHistories.AddRange(history);

        _audit.Add("employee.update", nameof(Employee), employee.Id.ToString(),
            details: new { employee.EmployeeNumber, changed, effectiveDate, reason = request.Reason });

        await SaveOrConflictAsync(cancellationToken);
        return await RequireDetailAsync(employee.Id, cancellationToken);
    }

    // ---------------------------------------------------------------- POST /employees/{id}/status (FR-ADM-024, BR-008)

    public async Task<EmployeeProfileDto> ChangeStatusAsync(Guid id, ChangeEmployeeStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Status is not (EmploymentStatus.Active or EmploymentStatus.Suspended or EmploymentStatus.Terminated))
            throw Validation(new Dictionary<string, List<string>>
            {
                ["status"] = ["Status must be Active, Suspended or Terminated."]
            });

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                       ?? throw new NotFoundException("The requested resource was not found.");

        if (employee.Id == _currentUser.EmployeeId && request.Status != EmploymentStatus.Active)
            throw new BusinessRuleException("You cannot suspend or terminate your own account.",
                new() { ["status"] = ["Self-service status changes are limited to reactivation."] });

        var effectiveDate = request.EffectiveDate == default ? _clock.Today : request.EffectiveDate;
        var oldStatus = employee.EmploymentStatus;

        employee.EmploymentStatus = request.Status;
        employee.StatusEffectiveDate = effectiveDate;
        if (request.Status == EmploymentStatus.Terminated) employee.LastWorkingDate = effectiveDate;

        _db.EmploymentHistories.Add(new EmploymentHistory
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            EventType = request.Status == EmploymentStatus.Terminated
                ? HistoryEventType.Termination
                : HistoryEventType.StatusChange,
            EffectiveDate = effectiveDate,
            OldValue = oldStatus.ToString(),
            NewValue = request.Status.ToString(),
            Reason = request.Reason,
            ApprovedBy = _currentUser.DisplayName,
            CreatedAt = _clock.UtcNow
        });

        // BR-008 / FR-EMP-006: access is revoked or restored with the employment status. A future
        // effective date defers revocation via UserAccount.AccessEndsOn instead of cutting access now.
        var accounts = await _db.UserAccounts.Where(u => u.EmployeeId == employee.Id).ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            switch (request.Status)
            {
                case EmploymentStatus.Active:
                    account.Status = UserStatus.Active;
                    account.AccessEndsOn = null;
                    break;
                case EmploymentStatus.Suspended:
                case EmploymentStatus.Terminated:
                    if (effectiveDate <= _clock.Today)
                    {
                        account.Status = request.Status == EmploymentStatus.Suspended
                            ? UserStatus.Suspended
                            : UserStatus.Terminated;
                    }
                    else
                    {
                        account.AccessEndsOn = effectiveDate.AddDays(-1);
                    }
                    break;
            }
            account.UpdatedAt = _clock.UtcNow;
        }

        _audit.Add("employee.status_changed", nameof(Employee), employee.Id.ToString(),
            details: new
            {
                employee.EmployeeNumber,
                oldStatus = oldStatus.ToString(),
                newStatus = request.Status.ToString(),
                effectiveDate,
                request.Reason,
                accountsAffected = accounts.Count
            });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Employee {Number} status {Old} -> {New} by {Actor}.",
            employee.EmployeeNumber, oldStatus, request.Status, _currentUser.EmployeeNumber);

        return await RequireDetailAsync(employee.Id, cancellationToken);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record DetailRow(
        Guid Id, string EmployeeNumber, string DisplayName, string WorkEmail, string? Phone,
        DateOnly JoinDate, DateOnly? ProbationEndDate, EmploymentType EmploymentType,
        EmploymentStatus EmploymentStatus, Guid DepartmentId, Guid LocationId, Guid? ManagerId,
        string? CostCentre, bool HasPhoto, string? Designation, string? Department, string? Location,
        string? ManagerName, string? LegalEntity, PersonalRow? Personal, SensitiveRow? Sensitive);

    private sealed record PersonalRow(string? PreferredName, string? PersonalEmail, DateOnly? DateOfBirth,
        Gender Gender, string? Address, string? EmergencyContactName, string? EmergencyContactPhone);

    private sealed record SensitiveRow(string? BankAccountNumber, string? BankName,
        string? NationalIdType, string? NationalIdNumber, string? TaxId);

    private Task<DetailRow?> LoadDetailRowAsync(Guid id, CancellationToken cancellationToken) =>
        (from e in _db.Employees.AsNoTracking()
         where e.Id == id
         select new DetailRow(
             e.Id, e.EmployeeNumber, e.DisplayName, e.WorkEmail, e.Phone,
             e.JoinDate, e.ProbationEndDate, e.EmploymentType, e.EmploymentStatus,
             e.DepartmentId, e.LocationId, e.ManagerId, e.CostCentre, e.PhotoStorageKey != null,
             e.Designation != null ? e.Designation.Name : null,
             e.Department != null ? e.Department.Name : null,
             e.Location != null ? e.Location.Name : null,
             e.Manager != null ? e.Manager.DisplayName : null,
             e.Organization != null ? e.Organization.Name : null,
             e.PersonalDetail == null
                 ? null
                 : new PersonalRow(e.PersonalDetail.PreferredName, e.PersonalDetail.PersonalEmail,
                     e.PersonalDetail.DateOfBirth, e.PersonalDetail.Gender, e.PersonalDetail.Address,
                     e.PersonalDetail.EmergencyContactName, e.PersonalDetail.EmergencyContactPhone),
             e.SensitiveData == null
                 ? null
                 : new SensitiveRow(e.SensitiveData.BankAccountNumber, e.SensitiveData.BankName,
                     e.SensitiveData.NationalIdType, e.SensitiveData.NationalIdNumber, e.SensitiveData.TaxId)))
        .FirstOrDefaultAsync(cancellationToken);

    private static EmployeeProfileDto MapDetail(DetailRow row, bool includeSensitive, bool unmask) => new()
    {
        EmployeeId = row.Id,
        EmployeeNumber = row.EmployeeNumber,
        DisplayName = row.DisplayName,
        PreferredName = row.Personal?.PreferredName,
        WorkEmail = row.WorkEmail,
        PersonalEmail = row.Personal?.PersonalEmail,
        Phone = row.Phone,
        DateOfBirth = row.Personal?.DateOfBirth,
        Gender = row.Personal?.Gender ?? Gender.PreferNotToSay,
        Address = row.Personal?.Address,
        EmergencyContactName = row.Personal?.EmergencyContactName,
        EmergencyContactPhone = row.Personal?.EmergencyContactPhone,
        Designation = row.Designation,
        Department = row.Department,
        DepartmentId = row.DepartmentId,
        Location = row.Location,
        LocationId = row.LocationId,
        ManagerName = row.ManagerName,
        ManagerEmployeeId = row.ManagerId,
        JoiningDate = row.JoinDate,
        EmploymentType = row.EmploymentType,
        EmploymentStatus = row.EmploymentStatus,
        ProbationEndDate = row.ProbationEndDate,
        LegalEntity = row.LegalEntity,
        CostCentre = row.CostCentre,
        AvatarUrl = row.HasPhoto ? $"{AvatarBase}{row.Id}/avatar" : null,
        BankAccountNumber = Masked(row.Sensitive?.BankAccountNumber, includeSensitive, unmask),
        BankName = Masked(row.Sensitive?.BankName, includeSensitive, unmask),
        NationalIdType = Masked(row.Sensitive?.NationalIdType, includeSensitive, unmask),
        NationalIdNumber = Masked(row.Sensitive?.NationalIdNumber, includeSensitive, unmask),
        TaxId = Masked(row.Sensitive?.TaxId, includeSensitive, unmask)
    };

    /// <summary>Hidden without document.sensitive; masked by default; plaintext only with ?unmask=true.</summary>
    private static string? Masked(string? value, bool includeSensitive, bool unmask)
    {
        if (value is null || !includeSensitive) return null;
        if (unmask) return value;
        return value.Length <= 4 ? "****" : "****" + value[^4..];
    }

    private async Task<EmployeeProfileDto> RequireDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await LoadDetailRowAsync(id, cancellationToken)
                  ?? throw new NotFoundException("The requested resource was not found.");
        var dto = MapDetail(row, includeSensitive: false, unmask: false);
        return dto with { Status = row.EmploymentStatus };
    }

    private static IOrderedQueryable<Employee> ApplySort(IQueryable<Employee> query, string? sort)
    {
        var parts = (sort ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var field = parts.Length > 0 ? parts[0].ToLowerInvariant() : "name";
        var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

        return field switch
        {
            "employeenumber" or "number" => descending
                ? query.OrderByDescending(e => e.EmployeeNumber)
                : query.OrderBy(e => e.EmployeeNumber),
            "department" => descending
                ? query.OrderByDescending(e => e.Department!.Name)
                : query.OrderBy(e => e.Department!.Name),
            "location" => descending
                ? query.OrderByDescending(e => e.Location!.Name)
                : query.OrderBy(e => e.Location!.Name),
            "designation" => descending
                ? query.OrderByDescending(e => e.Designation!.Name)
                : query.OrderBy(e => e.Designation!.Name),
            _ => descending
                ? query.OrderByDescending(e => e.DisplayName)
                : query.OrderBy(e => e.DisplayName)
        };
    }

    private async Task<string> GenerateEmployeeNumberAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        // Per-org code settings: prefix + zero-padded sequence (e.g. WINTECH-EMP001). Created lazily.
        var setting = await _db.Set<OrgEmployeeCodeSetting>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);

        if (setting is null)
        {
            var orgCode = await _db.Organizations.AsNoTracking().IgnoreQueryFilters()
                .Where(o => o.Id == organizationId)
                .Select(o => o.Code)
                .FirstOrDefaultAsync(cancellationToken) ?? "ORG";
            setting = new OrgEmployeeCodeSetting
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Prefix = $"{orgCode}-EMP",
                Padding = 3,
                NextSequence = 1
            };
            _db.Add(setting);
        }

        // Guard against stale NextSequence by scanning existing codes for this org.
        var numbers = await _db.Employees.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.OrganizationId == organizationId)
            .Select(e => e.EmployeeNumber)
            .ToListAsync(cancellationToken);

        var max = setting.NextSequence - 1;
        foreach (var number in numbers)
        {
            if (number.StartsWith(setting.Prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(number.AsSpan(setting.Prefix.Length), out var suffix))
                max = Math.Max(max, suffix);
        }

        var next = max + 1;
        setting.NextSequence = next + 1;
        return $"{setting.Prefix}{next.ToString().PadLeft(setting.Padding, '0')}";
    }

    private async Task<Dictionary<Guid, string>> DesignationNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await _db.Designations.AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);

    private async Task<Dictionary<Guid, string>> OrgUnitNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await _db.OrgUnits.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

    private async Task<Dictionary<Guid, string>> LocationNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await _db.Locations.AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);

    private async Task<Dictionary<Guid, string>> EmployeeNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.DisplayName, ct);

    /// <summary>Unique-index races surface as 409 rather than 500 (FR-ADM-021).</summary>
    private async Task SaveOrConflictAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException)
        {
            throw new ConflictException("The record conflicts with an existing employee (duplicate number or email).");
        }
    }

    private static RequestValidationException Validation(Dictionary<string, List<string>> errors) =>
        new("The request has validation errors.",
            errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value) =>
        value is { Length: > 500 } ? value[..500] : value;
}
