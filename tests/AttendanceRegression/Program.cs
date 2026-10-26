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
using Wintech.Hrms.Api.Modules.Attendance;
using Wintech.Hrms.Api.Modules.Chat;

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

sealed class TestUser(Guid employeeId) : ICurrentUser
{
    public bool Admin { get; set; }
    public bool IsAuthenticated => true;
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid? EmployeeId => employeeId;
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
