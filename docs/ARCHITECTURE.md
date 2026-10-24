# Wintech HRMS — architecture

## Solution shape

```
Wintech HRMS CRM/
├── docs/      BRD-derived contract + setup/architecture docs (single source of truth)
├── api/       .NET 9 Web API — modular monolith, EF Core (SQL Server / SQLite dev / InMemory tests)
└── mobile/    Flutter app (Android + iOS) built strictly against docs/API_CONTRACT.md
```

The API is deliberately a **modular monolith**: one deployable, one database, one transaction boundary,
but feature modules that never edit shared files. That keeps teams (and agents) working in parallel
without merge conflicts in `Program.cs` or `AppDbContext`.

## Auto-wiring conventions (why modules never edit shared files)

| You write | The framework does |
|---|---|
| `class LeaveService : ILeaveService, IAppService` | registers `ILeaveService -> LeaveService` (scoped) automatically |
| `class LeaveSeed : IDataSeeder` (`Order >= 100`) | runs after core seeding, in `Order` sequence, idempotently |
| `class LeaveRequest` in namespace `Wintech.Hrms.Api.Domain.*` with a public `Id` | discovered as an EF entity |
| `class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>` | applied automatically (`ToTable("lv_leave_requests")` keeps table names unique) |
| `class LeaveController` (`[Route("api/v1/leave-requests")]`) | mapped by `MapControllers()` |
| `class LeaveHandler : IApprovalHandler` (`RequestType = "Leave"`) | invoked by the approval engine when that workflow completes |

Consequences: `Program.cs`, `AppDbContext.cs`, `Permissions.cs` and the seeders are **core-owned and
frozen**; new features are additive files only. Entities are queried with `_db.Set<T>()`, so no
`DbSet` property is required.

## Request pipeline

```
UseErrorEnvelopeStatusCodes -> CorrelationIdMiddleware -> ExceptionHandlingMiddleware
-> Swagger(dev) -> CORS -> RateLimiter -> Authentication(JWT) -> AccountStatusMiddleware
-> Authorization (permission policies) -> Controllers
```

- **Correlation id** is attached to every response and stored on every audit row.
- **Errors**: any `ApiException` subclass becomes the documented envelope
  `{status,title,detail,errors,correlationId}`; DataAnnotation failures map to the same shape
  through `InvalidModelStateResponseFactory`.
- **AccountStatusMiddleware** cuts off suspended/terminated/expired accounts mid-session (BR-008).

## Security

- **JWT**: 15-minute access tokens + rotating refresh tokens (stored hashed, reuse revokes the whole
  token family), optional TOTP MFA for admin roles, PBKDF2 password hashing, login rate limiting.
- **RBAC**: permission codes live in `Security/Permissions.cs`; roles are seeded rows, checked from the
  database on every request so changes apply immediately. `[Permission("leave.approve")]` on an action
  builds its authorization policy on demand — no policy registration per code.
- **SuperAdmin** holds `*`; every other role holds an explicit permission list.
- **Audit**: append-only `AuditEvent` rows (rows may never be modified or deleted — enforced in
  `SaveChanges`). Mutations use `IAuditService.Add` (commits with the business change); sensitive reads
  and exports use `RecordAsync`.

## Approvals engine

One generic engine (`IApprovalService`) drives leave, attendance corrections, expenses, travel,
letters, profile changes, shift swaps and resignations:

1. A module calls `StartAsync(requestType, requestId, ...)`.
2. The engine loads the active workflow version, snapshots it onto the task, resolves the current
   assignee (manager / role / named user), sets `DueAt` from `SLADays` and notifies the approver.
3. Decisions (`Approve|Reject|Return|Delegate`) append `ApprovalAction` rows, enforce BR-004
   (no self-approval) and BR-007 (rejection reason), and advance or complete the workflow with
   optimistic concurrency on `Task.Version`.
4. On completion the matching `IApprovalHandler` (`RequestType` keyed) updates the owning domain
   record exactly once (UAT-004) and notifies the requester.

## Cross-module collaboration without cross-module references

Modules never reference each other's types. Two small contracts in `Common/` carry data across:

- `IDashboardSliceProvider` — each module contributes its own top-level keys to `GET /me/dashboard`
  (`attendance`, `leaveBalances`, `holidays`, `announcements`, `notificationsUnread`, …).
- `IReportDataProvider` — each module serves its own report rows for `GET /reports/{report}`
  (`headcount`, `approval-aging`, `attendance-exceptions`, `document-expiry`, `leave-liability`).

Both are discovered through `IAppService`, so adding a slice/report is an additive file.

## Data & deployment

- **Development**: SQLite file `api/App_Data/wintech-hrms.dev.db`, `EnsureCreated` + ordered seeders.
  Delete the file to rebuild schema and demo data.
- **Production**: SQL Server with retry-on-failure; schema created from the model (or migrations when
  the model stabilises). Queries stay provider-neutral (EF LINQ only).
- **Files** (avatars, receipts, letters) are stored under a private root with an extension allow-list
  and size cap (BR-010), streamed through an authenticated endpoint — never a public URL.
- **PDF** (payslips, letters) is generated server-side with QuestPDF.
- **Notifications**: `INotificationPublisher` has a logging fallback; the Notifications module's
  persistent implementation is registered later and takes precedence.
