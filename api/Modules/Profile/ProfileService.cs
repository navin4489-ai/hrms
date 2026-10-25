using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.Dashboard;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Files;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Profile;

namespace Wintech.Hrms.Api.Modules.Profile;

/// <summary>
/// "Me" self-service surface (contract §5, FR-EMP-010..025): personal/work profile, self-service
/// profile edits, sensitive change requests routed through the approval engine (FR-EMP-022),
/// avatar storage (BR-010), employment history, notification preferences (FR-EMP-121) and the
/// dashboard composed from core data plus every registered <see cref="IDashboardSliceProvider"/>.
/// </summary>
public sealed class ProfileService : IProfileService
{
    private const string AvatarUrl = "/api/v1/me/avatar";
    private static readonly string[] AvatarExtensions = [".jpg", ".jpeg", ".png"];

    /// <summary>FR-EMP-022: sensitive fields an employee may request to change (never applied directly).</summary>
    private static readonly string[] SensitiveChangeFields =
        ["BankAccount", "BankAccountNumber", "BankName", "LegalName", "NationalId", "NationalIdNumber", "TaxId"];

    /// <summary>FR-EMP-013: dashboard quick actions, gated by role entitlement at render time (FR-EMP-014).</summary>
    private static readonly string[] QuickActions =
        ["leave.request", "attendance.correct", "payslip.view", "case.create", "documents.view"];

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IPrivateFileStore _files;
    private readonly SecurityOptions _security;
    private readonly IReadOnlyList<IDashboardSliceProvider> _sliceProviders;
    private readonly IApprovalService? _approvals;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(AppDbContext db, ICurrentUser currentUser, IClock clock, IAuditService audit,
        IPrivateFileStore files, IOptions<SecurityOptions> security,
        IEnumerable<IDashboardSliceProvider> sliceProviders,
        // Resolved as a sequence so the API still runs before the Approvals module is installed
        // (the integrated solution always has exactly one implementation).
        IEnumerable<IApprovalService> approvalServices,
        ILogger<ProfileService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _files = files;
        _security = security.Value;
        _sliceProviders = sliceProviders.ToList();
        _approvals = approvalServices.FirstOrDefault();
        _logger = logger;
    }

    // ---------------------------------------------------------------- GET /me

    public async Task<EmployeeProfileDto> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();

        var row = await (
                from e in _db.Employees.AsNoTracking()
                where e.Id == employeeId
                select new
                {
                    e.Id, e.EmployeeNumber, e.DisplayName, e.WorkEmail, e.Phone,
                    e.JoinDate, e.ProbationEndDate, e.EmploymentType, e.EmploymentStatus,
                    e.DepartmentId, e.LocationId, e.ManagerId, e.CostCentre, e.PhotoStorageKey,
                    Designation = e.Designation != null ? e.Designation.Name : null,
                    Department = e.Department != null ? e.Department.Name : null,
                    Location = e.Location != null ? e.Location.Name : null,
                    ManagerName = e.Manager != null ? e.Manager.DisplayName : null,
                    LegalEntity = e.Organization != null ? e.Organization.Name : null,
                    Personal = e.PersonalDetail == null
                        ? null
                        : new
                        {
                            e.PersonalDetail.PreferredName, e.PersonalDetail.PersonalEmail,
                            e.PersonalDetail.DateOfBirth, e.PersonalDetail.Gender, e.PersonalDetail.Address,
                            e.PersonalDetail.EmergencyContactName, e.PersonalDetail.EmergencyContactPhone
                        }
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) throw new NotFoundException("No employee record is linked to your account.");

        return new EmployeeProfileDto
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
            AvatarUrl = row.PhotoStorageKey == null ? null : AvatarUrl
        };
    }

    // ---------------------------------------------------------------- PUT /me/profile (FR-EMP-021)

    public async Task<ProfileSavedResponse> UpdateProfileAsync(UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
                       ?? throw new NotFoundException("No employee record is linked to your account.");

        var personal = await _db.EmployeePersonalDetails
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId, cancellationToken);

        var created = personal is null;
        personal ??= new EmployeePersonalDetail
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            UpdatedAt = _clock.UtcNow
        };

        // Only non-sensitive self-service fields; sensitive fields must go through approvals.
        personal.PreferredName = NullIfWhiteSpace(request.PreferredName);
        personal.PersonalEmail = NullIfWhiteSpace(request.PersonalEmail);
        personal.Address = NullIfWhiteSpace(request.Address);
        personal.EmergencyContactName = NullIfWhiteSpace(request.EmergencyContactName);
        personal.EmergencyContactPhone = NullIfWhiteSpace(request.EmergencyContactPhone);
        personal.UpdatedAt = _clock.UtcNow;

        employee.Phone = NullIfWhiteSpace(request.Phone) ?? employee.Phone;

        if (created) _db.EmployeePersonalDetails.Add(personal);

        _audit.Add("profile.update", nameof(Employee), employee.Id.ToString(),
            details: new
            {
                preferredName = personal.PreferredName,
                phone = employee.Phone,
                personalEmail = personal.PersonalEmail,
                address = personal.Address,
                emergencyContactName = personal.EmergencyContactName,
                emergencyContactPhone = personal.EmergencyContactPhone
            });

        await _db.SaveChangesAsync(cancellationToken);
        return new ProfileSavedResponse("saved", "Profile updated.");
    }

    // ---------------------------------------------------------------- POST /me/sensitive-change-requests (FR-EMP-022)

    public async Task<SensitiveChangeResponse> RequestSensitiveChangeAsync(SensitiveChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();

        if (!SensitiveChangeFields.Contains(request.Field, StringComparer.OrdinalIgnoreCase))
            throw new RequestValidationException(errors: new()
            {
                ["field"] = [$"'{request.Field}' is not a changeable sensitive field. Allowed: {string.Join(", ", SensitiveChangeFields)}."]
            });

        var employee = await _db.Employees.AsNoTracking()
                           .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
                       ?? throw new NotFoundException("No employee record is linked to your account.");

        var change = new ProfileChangeRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Field = request.Field,
            RequestedValue = request.RequestedValue,
            Reason = request.Reason,
            Status = ProfileChangeStatus.Pending,
            RequestedAt = _clock.UtcNow
        };
        _db.ProfileChangeRequests.Add(change);
        await _db.SaveChangesAsync(cancellationToken);

        // Route into the approval workflow (HR verification, WF-PROFILE). The value stays masked
        // in every response until the workflow completes successfully (handled by the approver).
        if (_approvals is not null)
        {
            var task = await _approvals.StartAsync(
                new ApprovalStartRequest("ProfileChange", change.Id,
                    $"Sensitive profile change: {change.Field}", _currentUser.RequireUserId(), employeeId,
                    employee.DisplayName),
                cancellationToken);
            change.ApprovalTaskId = task.Id;
        }
        else
        {
            _logger.LogWarning(
                "Approval engine unavailable; ProfileChange {Id} stays pending without a workflow task.", change.Id);
        }

        _audit.Add("profile.sensitive_change_requested", nameof(ProfileChangeRequest), change.Id.ToString(),
            details: new { employeeId, field = change.Field, change.Reason });

        await _db.SaveChangesAsync(cancellationToken);
        return new SensitiveChangeResponse(change.Id, nameof(ProfileChangeStatus.Pending));
    }

    // ---------------------------------------------------------------- POST/GET /me/avatar (FR-EMP-023, BR-010)

    public async Task<AvatarResponse> SaveAvatarAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
                       ?? throw new NotFoundException("No employee record is linked to your account.");

        var maxBytes = _security.AvatarMaxUploadBytes > 0 ? _security.AvatarMaxUploadBytes : 2 * 1024 * 1024;
        if (file.Length == 0)
            throw new RequestValidationException(errors: new() { ["file"] = ["A photo is required."] });
        if (file.Length > maxBytes)
            throw new RequestValidationException("File is too large.",
                new() { ["file"] = [$"Maximum avatar size is {maxBytes / 1024 / 1024} MB."] });

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AvatarExtensions.Contains(extension))
            throw new RequestValidationException("File type is not allowed.",
                new() { ["file"] = ["Allowed types: .jpg, .jpeg, .png."] });

        await using var stream = file.OpenReadStream();
        var stored = await _files.SaveAsync(stream, file.FileName, maxBytes, AvatarExtensions, cancellationToken);

        var previousKey = employee.PhotoStorageKey;
        employee.PhotoStorageKey = stored.StorageKey;

        _audit.Add("profile.avatar_uploaded", nameof(Employee), employee.Id.ToString(),
            details: new { stored.StorageKey, stored.SizeBytes, stored.OriginalFileName });

        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previousKey) && previousKey != stored.StorageKey)
        {
            try { await _files.DeleteAsync(previousKey, cancellationToken); }
            catch (Exception exception) { _logger.LogWarning(exception, "Could not delete previous avatar {Key}.", previousKey); }
        }

        return new AvatarResponse(AvatarUrl);
    }

    public async Task<AvatarStreamResult> OpenAvatarAsync(CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var storageKey = await _db.Employees.AsNoTracking()
                             .Where(e => e.Id == employeeId)
                             .Select(e => e.PhotoStorageKey)
                             .FirstOrDefaultAsync(cancellationToken)
                         ?? throw new NotFoundException("You have not uploaded a profile photo.");

        var file = await _files.OpenReadAsync(storageKey, cancellationToken);
        return new AvatarStreamResult(file.Content, file.ContentType);
    }

    // ---------------------------------------------------------------- GET /me/employment-history (FR-EMP-025)

    public async Task<IReadOnlyList<EmploymentHistoryItemDto>> GetEmploymentHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var exists = await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId, cancellationToken);
        if (!exists) throw new NotFoundException("No employee record is linked to your account.");

        return await _db.EmploymentHistories.AsNoTracking()
            .Where(h => h.EmployeeId == employeeId)
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

    // ---------------------------------------------------------------- GET/PUT /me/notification-preferences (FR-EMP-121)

    public async Task<IReadOnlyList<NotificationPreferenceDto>> GetNotificationPreferencesAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var rows = await _db.Set<NotificationPreference>().AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            // Lazy defaults for accounts created after seeding.
            foreach (var (category, mandatory) in NotificationCategories.Catalog)
            {
                _db.Set<NotificationPreference>().Add(new NotificationPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Category = category,
                    InApp = true,
                    Push = true,
                    Email = mandatory,
                    Mandatory = mandatory,
                    UpdatedAt = _clock.UtcNow
                });
            }
            await _db.SaveChangesAsync(cancellationToken);
            rows = await _db.Set<NotificationPreference>().AsNoTracking()
                .Where(p => p.UserId == userId)
                .ToListAsync(cancellationToken);
        }

        return rows
            .OrderBy(p => CatalogIndex(p.Category))
            .Select(p => new NotificationPreferenceDto
            {
                Category = p.Category,
                InApp = p.InApp,
                Push = p.Push,
                Email = p.Email,
                Mandatory = p.Mandatory
            })
            .ToList();
    }

    public async Task UpdateNotificationPreferencesAsync(UpdateNotificationPreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var rows = await _db.Set<NotificationPreference>()
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            foreach (var (category, mandatory) in NotificationCategories.Catalog)
            {
                _db.Set<NotificationPreference>().Add(new NotificationPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Category = category,
                    InApp = true,
                    Push = true,
                    Email = mandatory,
                    Mandatory = mandatory,
                    UpdatedAt = _clock.UtcNow
                });
            }
            await _db.SaveChangesAsync(cancellationToken);
            rows = await _db.Set<NotificationPreference>()
                .Where(p => p.UserId == userId)
                .ToListAsync(cancellationToken);
        }

        var errors = new Dictionary<string, List<string>>();
        var byCategory = rows.ToDictionary(p => p.Category, StringComparer.OrdinalIgnoreCase);

        foreach (var item in request.Items)
        {
            var category = item.Category ?? string.Empty;
            if (!NotificationCategories.IsKnown(category))
            {
                errors[category] = ["Unknown notification category."];
                continue;
            }

            var canonical = NotificationCategories.Normalize(category);
            if (!byCategory.TryGetValue(canonical, out var row)) continue;

            var disabling = (item.InApp == false || item.Push == false || item.Email == false) &&
                            (row.Mandatory || NotificationCategories.IsMandatory(canonical));
            if (disabling)
            {
                errors[canonical] = ["Mandatory notifications cannot be disabled (FR-EMP-121)."];
                continue;
            }

            if (item.InApp.HasValue) row.InApp = item.InApp.Value;
            if (item.Push.HasValue) row.Push = item.Push.Value;
            if (item.Email.HasValue) row.Email = item.Email.Value;
            row.Mandatory = row.Mandatory || NotificationCategories.IsMandatory(canonical);
            row.UpdatedAt = _clock.UtcNow;
        }

        if (errors.Count > 0)
            throw new BusinessRuleException("Notification preferences were not saved.",
                errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));

        _audit.Add("profile.notification_preferences_updated", "NotificationPreference", userId.ToString(),
            details: new { items = request.Items.Select(i => i.Category).ToArray() });

        await _db.SaveChangesAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- GET /me/dashboard (FR-EMP-010..014)

    public async Task<IReadOnlyDictionary<string, object?>> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.RequireUserId();
        var employeeId = RequireEmployeeId();
        var today = _clock.Today;

        var employee = await (
                from e in _db.Employees.AsNoTracking()
                where e.Id == employeeId
                select new
                {
                    e.EmployeeNumber, e.DisplayName, e.EmploymentStatus, e.FirstName,
                    PreferredName = e.PersonalDetail != null ? e.PersonalDetail.PreferredName : null,
                    Designation = e.Designation != null ? e.Designation.Name : null,
                    Department = e.Department != null ? e.Department.Name : null,
                    Location = e.Location != null ? e.Location.Name : null,
                    ManagerName = e.Manager != null ? e.Manager.DisplayName : null
                })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("No employee record is linked to your account.");

        // Pending approvals waiting on the caller (assigned directly or through one of their roles).
        var roles = _currentUser.Roles.ToArray();
        var pendingApprovals = await _db.ApprovalTasks.AsNoTracking()
            .CountAsync(t => t.Status == "Pending" &&
                             (t.CurrentAssigneeUserId == userId ||
                              (t.CurrentAssigneeRole != null && roles.Contains(t.CurrentAssigneeRole))),
                cancellationToken);

        var myRequestRows = await _db.ApprovalTasks.AsNoTracking()
            .Where(t => t.RequesterUserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .Select(t => new { t.Id, t.RequestType, t.Title, t.Status, t.CreatedAt, t.Amount })
            .ToListAsync(cancellationToken);

        var myRequests = myRequestRows
            .Select(t => (object)new
            {
                id = t.Id,
                type = t.RequestType,
                title = t.Title,
                status = t.Status,
                date = DateOnly.FromDateTime(t.CreatedAt),
                amount = t.Amount,
                deepLink = DeepLink(t.Id)
            })
            .ToList();

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["greeting"] = $"{Greeting(_clock.UtcNow.Hour)}, {employee.PreferredName ?? employee.FirstName}",
            ["today"] = today,
            ["employee"] = new
            {
                employeeNumber = employee.EmployeeNumber,
                displayName = employee.DisplayName,
                designation = employee.Designation,
                department = employee.Department,
                location = employee.Location,
                managerName = employee.ManagerName,
                employmentStatus = employee.EmploymentStatus
            },
            ["pendingApprovals"] = pendingApprovals,
            ["myRequests"] = myRequests,
            ["quickActions"] = QuickActions
        };

        // Merge slices contributed by feature modules (attendance, leave balances, holidays,
        // announcements, unread notifications, ...) in ascending Order; never overwrite a key.
        var context = new DashboardContext(userId, employeeId, today);
        foreach (var provider in _sliceProviders.OrderBy(p => p.Order).ThenBy(p => p.GetType().Name))
        {
            IReadOnlyDictionary<string, object?> slice;
            try
            {
                slice = await provider.GetSliceAsync(context, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Dashboard slice provider {Provider} failed; continuing without it.",
                    provider.GetType().Name);
                continue;
            }

            foreach (var pair in slice) payload.TryAdd(pair.Key, pair.Value);
        }

        return payload;
    }

    // ---------------------------------------------------------------- helpers

    private Guid RequireEmployeeId() =>
        _currentUser.EmployeeId ?? throw new NotFoundException("No employee record is linked to your account.");

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Greeting(int hour) =>
        hour switch
        {
            < 12 => "Good morning",
            < 17 => "Good afternoon",
            _ => "Good evening"
        };

    private static int CatalogIndex(string category)
    {
        var index = NotificationCategories.Catalog.ToList()
            .FindIndex(c => string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    // The dashboard row is an ApprovalTask, not the underlying module request.
    // Link to the task ID so the detail endpoint can show status/history to its requester.
    private static string DeepLink(Guid taskId) => $"/approvals/{taskId}";
}
