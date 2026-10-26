using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Files;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Tenancy;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Attendance;
using Wintech.Hrms.Api.Domain.Admin;
using Wintech.Hrms.Api.Domain.Chat;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Organization;
using Wintech.Hrms.Api.Domain.Payroll;
using Wintech.Hrms.Api.Modules.Attendance;
using Wintech.Hrms.Api.Modules.Chat;
using Wintech.Hrms.Api.Modules.Organizations;
using Wintech.Hrms.Api.Modules.Payroll;

// Isolated, deterministic regression: never touches production or local HR data.
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
await db.Database.ExecuteSqlRawAsync("DROP TABLE att_punch_events");
await AttendanceSchemaCompatibility.EnsureAsync(db, NullLogger.Instance);
await AttendanceSchemaCompatibility.EnsureAsync(db, NullLogger.Instance);
Assert(await db.Set<AttendancePunchEvent>().CountAsync() == 0, "legacy upgrade creates table and is repeatable");
await db.Database.ExecuteSqlRawAsync("DROP TABLE chat_attachment_content");
await TenantSchemaCompatibility.EnsureAsync(db, NullLogger.Instance);
await TenantSchemaCompatibility.EnsureAsync(db, NullLogger.Instance);
Assert(await db.Set<ChatAttachmentContent>().CountAsync() == 0, "durable attachment schema upgrade is repeatable");

var org = new Organization { Id = Guid.NewGuid(), Code = "TEST", Name = "Regression" };
var department = new OrgUnit { Id = Guid.NewGuid(), OrganizationId = org.Id, Code = "OPS", Name = "Operations" };
var location = new Location { Id = Guid.NewGuid(), OrganizationId = org.Id, Code = "HQ", Name = "HQ" };
var designation = new Designation { Id = Guid.NewGuid(), OrganizationId = org.Id, Code = "ENG", Name = "Engineer" };
var employee = new Employee
{
    Id = Guid.NewGuid(), OrganizationId = org.Id, EmployeeNumber = "TEST-EMP001",
    FirstName = "Test", LastName = "Employee", WorkEmail = "attendance@example.test",
    DepartmentId = department.Id, LocationId = location.Id, DesignationId = designation.Id,
    JoinDate = new DateOnly(2026, 1, 1)
};
db.AddRange(org, department, location, designation, employee);
db.Add(new WorkShift
{
    Id = Guid.NewGuid(), Code = "TEST", Name = "General", IsActive = true, IsDefault = true,
    StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(18, 0),
    WeekDays = "Mon,Tue,Wed,Thu,Fri", BreakMinutes = 0
});
await db.SaveChangesAsync();
db.ChangeTracker.Clear();

var clock = new TestClock();
var user = new TestUser(employee.Id);
var files = new TestFiles();
var service = new AttendanceService(db, user, clock, new AuditService(db, user, clock), files, new SecurityOptions());
var bytes = new byte[] { 1, 2, 3, 4 };
PunchForm Form() => new()
{
    Source = "Mobile", Latitude = 18.52, Longitude = 73.85, AccuracyMeters = 12,
    LocationName = "Shivajinagar, Pune, Maharashtra",
    Photo = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", "selfie.jpg")
};

var checkIn = await service.PunchAsync(Form());
Assert(checkIn.CheckIn is not null && checkIn.CheckOut is null, "check-in succeeds after legacy upgrade");
db.ChangeTracker.Clear();
clock.UtcNow = clock.UtcNow.AddHours(8);
var checkOut = await service.PunchAsync(Form());
Assert(checkOut.CheckOut is not null, "checkout succeeds with advanced test clock (no waiting)");
db.ChangeTracker.Clear();
var events = await db.Set<AttendancePunchEvent>().OrderBy(e => e.CapturedAtUtc).ToListAsync();
Assert(events.Count == 2 && events[0].Direction == PunchDirection.CheckIn && events[1].Direction == PunchDirection.CheckOut,
    "both punch directions stored");
Assert(events.All(e => e.Latitude == 18.52 && e.LocationName == "Shivajinagar, Pune, Maharashtra" && e.PhotoStorageKey is not null),
    "selfie, coordinates and reverse-geocoded location stored for both punches");
var today = await service.GetTodayAsync(null);
Assert(today.CheckIn is not null && today.CheckOut is not null, "today reflects completed attendance");
Assert(!today.GetType().GetProperties().Any(p => p.Name is "Latitude" or "PhotoStorageKey"), "employee response excludes capture data");
try
{
    await service.GetPunchEventsAsync(checkIn.WorkDate, checkIn.WorkDate, null, null, null, new PageRequest());
    throw new Exception("Employee read admin capture data");
}
catch (ForbiddenException) { Console.WriteLine("PASS: employee denied admin capture data"); }
user.Admin = true;
await using (var photo = (await service.OpenPunchPhotoAsync(events[0].Id)).Content)
    Assert(photo.Length == bytes.Length, "admin can read captured selfie");

// Best-effort: missing permission/cancelled camera is represented by no photo/location.
clock.UtcNow = clock.UtcNow.AddDays(1);
db.ChangeTracker.Clear();
await service.PunchAsync(new PunchForm());
Assert(await db.Set<AttendancePunchEvent>().CountAsync() == 3, "punch without camera/location still succeeds");
db.AddRange(
    new SystemConfiguration { Id = Guid.NewGuid(), Key = "Attendance.CapturePhoto", Value = "false", Type = "Bool" },
    new SystemConfiguration { Id = Guid.NewGuid(), Key = "Attendance.CaptureLocation", Value = "false", Type = "Bool" });
await db.SaveChangesAsync();
clock.UtcNow = clock.UtcNow.AddDays(1);
db.ChangeTracker.Clear();
var noCapturePunch = await service.PunchAsync(Form());
var noCapture = await db.Set<AttendancePunchEvent>().SingleAsync(e => e.WorkDate == noCapturePunch.WorkDate);
Assert(noCapture.Latitude is null && noCapture.PhotoStorageKey is null,
    "admin capture-off setting is respected even if a client submits capture data");
Console.WriteLine("Attendance regression passed.");

// Durable attachment regression: bytes remain readable with a brand-new file
// store, proving that replacing the service's ephemeral disk cannot lose them.
var conversation = new Conversation
{
    Id = Guid.NewGuid(), OrganizationId = org.Id, Type = ConversationType.Group,
    Name = "Regression", CreatedAt = clock.UtcNow, LastMessageAt = clock.UtcNow
};
db.Add(conversation);
db.Add(new ConversationMember
{
    Id = Guid.NewGuid(), OrganizationId = org.Id, ConversationId = conversation.Id,
    EmployeeId = employee.Id, JoinedAt = clock.UtcNow, IsAdmin = true
});
await db.SaveChangesAsync();
db.ChangeTracker.Clear();
var chat = new ChatService(db, user, clock, files, new TestHub(), new TestPush(), NullLogger<ChatService>.Instance);
foreach (var (name, kind) in new[] { ("image.png", ChatAttachmentKind.Image), ("document.pdf", ChatAttachmentKind.File) })
{
    var sent = await chat.SendAttachmentAsync(conversation.Id, new MemoryStream(bytes), name, kind, null);
    db.ChangeTracker.Clear();
    var restartedChat = new ChatService(db, user, clock, new TestFiles(), new TestHub(), new TestPush(), NullLogger<ChatService>.Instance);
    var download = await restartedChat.OpenAttachmentAsync(sent.Id);
    using var downloaded = new MemoryStream();
    await download.Content.CopyToAsync(downloaded);
    await download.Content.DisposeAsync();
    Assert(downloaded.ToArray().SequenceEqual(bytes) && download.FileName == name,
        $"{name} downloads byte-for-byte after private filesystem replacement");
    Assert(download.ContentType == (kind == ChatAttachmentKind.Image ? "image/png" : "application/pdf"),
        $"{name} has correct content type");
}
var member = await db.Set<ConversationMember>().SingleAsync();
member.LeftAt = clock.UtcNow;
await db.SaveChangesAsync();
try
{
    await chat.OpenAttachmentAsync((await db.Set<ChatMessage>().FirstAsync()).Id);
    throw new Exception("Former member could download attachment");
}
catch (ForbiddenException) { Console.WriteLine("PASS: former members denied attachment download"); }
Console.WriteLine("Attachment regression passed.");

// ------------------------------------------------------------------ payroll & tax regression
// Covers the finance-maintained pay details: automatic Indian income-tax (TDS) estimation
// and payslip generation from those pay details. Deterministic — no waiting, no prod data.
var payrollUser = new TestUser(employee.Id) { Admin = true };
var payroll = new PayrollService(db, payrollUser, new AuditService(db, payrollUser, clock), clock,
    new TestOrganizations());

// Tax calculator: ₹12L new-regime FY2026 gross is fully rebated (Budget 2025 §87A).
var tax12 = IncomeTaxCalculator.Compute(TaxRegime.New, 2026, 1_200_000m);
Assert(tax12.TaxableIncome == 1_125_000m && tax12.AnnualTax == 0m && tax12.MonthlyTds == 0m,
    "new regime FY2026: 12L gross -> zero tax after 87A rebate");

// ₹18L new-regime: taxable 17.25L -> 1.45L tax + 4% cess, no rebate.
var tax18 = IncomeTaxCalculator.Compute(TaxRegime.New, 2026, 1_800_000m);
Assert(tax18.TaxBeforeRebate == 145_000m && tax18.Rebate87A == 0m && tax18.Cess == 5_800m
       && tax18.AnnualTax == 150_800m && tax18.MonthlyTds == 12_566.67m,
    "new regime FY2026: 18L gross -> 150,800 annual tax, 12,566.67 monthly TDS");

// Old regime honours Chapter VI-A (80C etc.) and the ₹50k standard deduction.
var taxOld = IncomeTaxCalculator.Compute(TaxRegime.Old, 2026, 1_000_000m, chapterViA: 150_000m);
Assert(taxOld.TaxableIncome == 800_000m && taxOld.AnnualTax == 75_400m,
    "old regime: 80C reduces taxable income and tax is computed on old slabs");

// A full calendar month pays exactly one month; a half month pays half.
Assert(PayrollService.PeriodMonthFactor(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)) == 1m,
    "full-month payroll period has a 1.0 month factor");
Assert(PayrollService.PeriodMonthFactor(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15)) == 15m / 31m,
    "half-month payroll period prorates by covered days");

// Finance fills pay details; tax is derived from them, never typed in.
// ₹20L package: new-regime FY2026 taxable 19.25L -> 1.85L slab tax, no rebate, +4% cess.
var paySave = await payroll.SaveSalaryStructureAsync(employee.Id, new SaveSalaryStructureRequest
{
    BasicAnnual = 1_200_000m, HraAnnual = 480_000m, DaAnnual = 120_000m,
    SpecialAllowanceAnnual = 200_000m, TaxRegime = "New", ProfessionalTaxMonthly = 200m
});
db.ChangeTracker.Clear();
Assert(paySave.AnnualCtc == 2_000_000m && paySave.MonthlyGross == 166_666.67m,
    "saving pay details computes CTC and monthly gross from the components");
Assert(paySave.Tax.TaxableIncome == 1_925_000m && paySave.Tax.AnnualTax == 192_400m
       && paySave.Tax.MonthlyTds == 16_033.33m,
    "saving pay details computes the annual tax and its monthly TDS");

// Employees never see pay details; only payroll.manage (finance/admin) does.
try
{
    payrollUser.Admin = false;
    await payroll.GetSalaryStructuresAsync(null);
    throw new Exception("Employee read salary structures");
}
catch (ForbiddenException) { Console.WriteLine("PASS: employee denied pay details"); }
payrollUser.Admin = true;

// Employee self-service: own pay details only, readable without admin rights,
// with the tax recomputed from the saved components.
payrollUser.Admin = false;
payrollUser.EmployeeId = employee.Id;
var myPay = await payroll.GetMySalaryAsync();
Assert(myPay.EmployeeId == employee.Id && myPay.AnnualCtc == 2_000_000m
       && myPay.Tax.AnnualTax == 192_400m && myPay.Tax.MonthlyTds == 16_033.33m,
    "employee reads own pay details with the automatic tax");

// Accounts not linked to an employee are refused; linked employees without pay
// details get 404 rather than an empty/incorrect figure.
payrollUser.EmployeeId = null;
try
{
    await payroll.GetMySalaryAsync();
    throw new Exception("Unlinked account read pay details");
}
catch (ForbiddenException) { Console.WriteLine("PASS: unlinked account denied own pay details"); }
payrollUser.EmployeeId = Guid.NewGuid();
try
{
    await payroll.GetMySalaryAsync();
    throw new Exception("Missing pay details did not 404");
}
catch (NotFoundException) { Console.WriteLine("PASS: own pay details 404 before finance records them"); }
payrollUser.EmployeeId = employee.Id;
payrollUser.Admin = true;

// Create an October period and generate payslips automatically from the pay details.
var october = new PayrollPeriod
{
    Id = Guid.NewGuid(), Label = "Oct 2026", StartDate = new DateOnly(2026, 10, 1),
    EndDate = new DateOnly(2026, 10, 31), CutOffDate = new DateOnly(2026, 11, 5),
    Status = PayrollPeriodStatus.Open, CreatedAt = clock.UtcNow
};
db.Add(october);
await db.SaveChangesAsync();
db.ChangeTracker.Clear();

var generate = await payroll.GeneratePayslipsAsync(october.Id);
db.ChangeTracker.Clear();
Assert(generate.Generated == 1, "generate creates a payslip for the employee with pay details");

var payslip = await db.Set<Payslip>().Include(p => p.Components).SingleAsync(p => p.PeriodId == october.Id);
var earnings = payslip.Components.Where(c => c.Kind == PayslipComponentKind.Earning).ToList();
var deductions = payslip.Components.Where(c => c.Kind == PayslipComponentKind.Deduction).ToList();
Assert(earnings.Any(c => c.Component == "Basic" && c.Amount == 100_000m)
       && earnings.Any(c => c.Component == "HRA" && c.Amount == 40_000m),
    "generated payslip has Basic and HRA earnings from the annual structure");
Assert(deductions.Any(c => c.Component == "Employee PF" && c.Amount == 1_800m),
    "generated payslip computes employee PF at 12% of basic capped at the 15k wage ceiling");
Assert(deductions.Any(c => c.Component == "Professional Tax" && c.Amount == 200m),
    "generated payslip carries the configured professional tax");
Assert(deductions.Any(c => c.Component == "Income Tax (TDS)" && c.Amount == paySave.Tax.MonthlyTds),
    "generated payslip embeds the automatic monthly TDS");
Assert(payslip.Gross == 166_666.67m
       && payslip.NetPay == Math.Round(payslip.Gross - payslip.TotalDeductions, 2),
    "generated payslip net pay equals gross less deductions");

// Re-running generation must not duplicate payslips.
try
{
    await payroll.GeneratePayslipsAsync(october.Id);
    throw new Exception("Regenerate duplicated payslips");
}
catch (ConflictException) { Console.WriteLine("PASS: regenerate refuses once every payslip exists"); }
Console.WriteLine("Payroll & tax regression passed.");

static void Assert(bool result, string label)
{
    if (!result) throw new Exception(label);
    Console.WriteLine($"PASS: {label}");
}

sealed class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 26, 3, 30, 0, DateTimeKind.Utc);
    public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    public DateOnly TodayIn(string timeZoneId) => Today;
}

sealed class TestUser(Guid? employeeId) : ICurrentUser
{
    public bool Admin { get; set; }
    public bool IsAuthenticated => true;
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid? EmployeeId { get; set; } = employeeId;
    public string EmployeeNumber => "TEST-EMP001";
    public string DisplayName => "Test Employee";
    public string? WorkEmail => "attendance@example.test";
    public IReadOnlyCollection<string> Roles => Admin ? ["SuperAdmin"] : ["Employee"];
    public IReadOnlyCollection<string> Permissions => Admin ? ["*"] : [];
    public string? RemoteIpAddress => null;
    public string? CorrelationId => null;
    public bool HasPermission(string permission) => Admin;
    public bool IsInRole(string role) => Roles.Contains(role);
    public Guid RequireUserId() => UserId;
    public void EnsureActive() { }
}

sealed class TestFiles : IPrivateFileStore
{
    private readonly Dictionary<string, byte[]> _files = [];
    public async Task<StoredFile> SaveAsync(Stream content, string originalFileName, long maxBytes,
        IReadOnlyCollection<string> allowedExtensions, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var key = Guid.NewGuid().ToString();
        _files[key] = buffer.ToArray();
        return new StoredFile(key, originalFileName, "image/jpeg", buffer.Length);
    }
    public Task<StoredFileStream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
        => Task.FromResult(new StoredFileStream(new MemoryStream(_files[storageKey]), "image/jpeg", "selfie.jpg", _files[storageKey].Length));
    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        _files.Remove(storageKey);
        return Task.CompletedTask;
    }
}

sealed class TestHub : IHubContext<ChatHub>
{
    public IHubClients Clients => null!; // No live socket connections in this regression.
    public IGroupManager Groups => null!;
}

sealed class TestPush : IPushSender
{
    public Task SendMessageAsync(Guid recipientEmployeeId, string senderName, string preview,
        Guid conversationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendIncomingCallAsync(Guid recipientEmployeeId, string callerName, string callerEmployeeId,
        string callId, string kind, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Payroll module is enabled so the payroll regression can run end to end.</summary>
sealed class TestOrganizations : IOrganizationService
{
    public Task<IReadOnlyList<OrgModuleFlagDto>> GetMyModulesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<OrgModuleFlagDto>>([
            new() { Key = OrgModules.Payroll, Label = "Payroll & Payslips", Enabled = true }
        ]);
    public Task<IReadOnlyList<OrganizationDto>> ListAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<OrganizationDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<OrganizationDetailDto> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<OrganizationDetailDto> UpdateAsync(Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<OrganizationDetailDto> UpdateModulesAsync(Guid id, UpdateOrgModulesRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
