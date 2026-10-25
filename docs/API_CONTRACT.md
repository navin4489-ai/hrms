# Wintech HRMS — API Contract (v1)

Source of truth for the .NET API and the Flutter mobile app.
Derived from `Wintech_HRMS_BRD.docx` (FR-EMP / FR-ADM / FR-MGR IDs referenced per endpoint).

- Base URL (local dev): `http://localhost:5080`
- All endpoints are prefixed `/api/v1`. JSON only, camelCase keys.
- Timestamps: ISO-8601 UTC with `Z` (`2026-10-24T09:02:00Z`). Business dates: `YYYY-MM-DD`. Times: `HH:mm`.
- Enums serialize as **strings**.
- Auth header: `Authorization: Bearer <accessToken>`.
- Header `X-Correlation-Id` is echoed on every response (client may supply one).

---

## 1. Response conventions

**Single object** -> `200`/`201` with the DTO as body.

**Paged list** -> `200` with:

```json
{ "items": [ ... ], "page": 1, "pageSize": 20, "total": 42, "hasNextPage": true }
```

Query paging params: `page` (default 1), `pageSize` (default 20, max 100). Search: `q`. Sort: `sort` (e.g. `name asc`).

**Plain list** (small, fixed) -> `200` with a JSON array.

**Error** -> non-2xx with:

```json
{
  "status": 422,
  "title": "Validation failed",
  "detail": "The request has validation errors.",
  "errors": { "startDate": ["End date must be on or after start date."] },
  "correlationId": "0HN..."
}
```

| Status | Meaning |
|---|---|
| 400 | Validation failed (`errors` map present) |
| 401 | Missing/expired token or bad credentials (never reveals whether a user exists) |
| 403 | Authenticated but not permitted (wrong permission, record scope, inactive account) |
| 404 | Not found, or exists but out of the caller's scope (no existence leak) |
| 409 | Conflict / invalid state transition (duplicate punch, already decided, concurrency) |
| 422 | Business-rule rejection (leave balance, notice period, self-approval) |
| 429 | Rate limited (`Retry-After` header set) |
| 500 | Server error (`detail` is generic; `correlationId` for support) |

---

## 2. Demo users (seeded, company "Wintech Solutions Pvt Ltd")

Development seed passwords: `Passw0rd!`. Non-development deployments use the configured seed password
or the one-time admin bootstrap described in `docs/SETUP.md`. Login field `username` accepts username,
work email, or employee number.

| username | employeeNo | name | role(s) | reports to |
|---|---|---|---|---|
| admin | EMP001 | Ravi Sharma | SuperAdmin | — |
| hr | EMP003 | Meera Iyer | HR Head, HR Executive | EMP001 |
| hrexec | EMP004 | Sandeep Rao | HR Executive | EMP003 |
| manager | EMP002 | Anil Kumar | LineManager | EMP001 |
| payroll | EMP005 | Kavita Menon | PayrollFinance | EMP001 |
| employee | EMP006 | Priya Nair | Employee | EMP002 |
| employee2 | EMP007 | Rohit Verma | Employee | EMP002 |
| auditor | EMP008 | Joseph Mathew | Auditor | EMP001 |

Demo org: Engineering, HR, Finance, Sales, Operations. Locations: Bengaluru HQ, Mumbai, Remote.
Designations: Software Engineer, Senior Software Engineer, HR Executive, Accountant, Sales Executive, Manager.

---

## 3. Permissions (RBAC)

Codes used by `[Permission("...")]` on endpoints. Mobile uses the permission list from login to gate UI (server always enforces).

`employee.view` `employee.create` `employee.edit` `employee.manage` `employee.export`
`attendance.view.team` `attendance.manage`
`leave.approve` `leave.manage`
`payroll.manage` `payroll.publish` `payslip.view.any`
`document.manage` `document.sensitive`
`expense.approve` `expense.finance`
`asset.manage`
`case.manage` `case.view.restricted`
`announcement.create`
`performance.manage` `training.manage`
`admin.users` `admin.roles` `admin.config`
`audit.view` `report.view` `report.export`

Roles: `Employee`, `LineManager`, `HRExecutive`, `HRHead`, `PayrollFinance`, `ITAssetAdmin`, `SystemAdmin`, `Auditor`, `SuperAdmin` (SuperAdmin holds `*`).

---

## 4. Auth (`/api/v1/auth`) — FR-EMP-001..007

### POST /auth/login — `[AllowAnonymous]`
```json
{ "username": "employee", "password": "Passw0rd!", "deviceName": "Pixel 9" }
```
200 (no MFA):
```json
{
  "accessToken": "eyJ...",
  "refreshToken": "opaque-base64url",
  "expiresIn": 900,
  "mfaRequired": false,
  "user": {
    "id": "guid", "employeeId": "guid", "employeeNumber": "EMP006",
    "displayName": "Priya Nair", "workEmail": "priya.nair@wintech.example",
    "designation": "Software Engineer", "department": "Engineering",
    "roles": ["Employee"], "permissions": ["leave.request"]
  }
}
```
200 with MFA: `{ "mfaRequired": true, "mfaToken": "..." }` -> POST /auth/mfa/verify `{ "mfaToken", "code" }` -> same 200 as above.
401 for bad credentials (generic message, never reveals existence). 403 `{title:"Account not active"}` when credentials are correct but user is suspended/terminated (FR-EMP-006).

### POST /auth/refresh — `[AllowAnonymous]` `{ "refreshToken": "..." }` -> same 200 token pair (rotation; reuse revokes the family).
### POST /auth/logout `{ "refreshToken": "..." }` -> 204 (idempotent).
### GET /auth/me -> current profile, roles and permissions from the database (authenticated).
### GET /auth/sessions -> `[{ "id", "deviceName", "ipAddress", "createdAt", "lastUsedAt", "current": true }]`
### DELETE /auth/sessions/{id} -> 204 (remote revocation of that refresh session; access token dies within its 15-min TTL).
### POST /auth/forgot-password `{ "username" }` -> 202 always (FR-EMP-002, no existence leak). Dev: token is logged by the API.
### POST /auth/reset-password `{ "token", "newPassword" }` -> 204 | 400 invalid/expired.
### POST /auth/change-password `{ "currentPassword", "newPassword" }` -> 204 | 400.
### POST /auth/mfa/setup -> `{ "secret", "otpauthUri" }`; POST /auth/mfa/enable `{ "code" }` -> 204; POST /auth/mfa/disable `{ "code" }` -> 204.

---

## 5. Me & dashboard — FR-EMP-010..025

### GET /me -> personal + work profile
```json
{
  "employeeId": "guid", "employeeNumber": "EMP006", "displayName": "Priya Nair",
  "preferredName": "Priya", "workEmail": "priya.nair@wintech.example",
  "personalEmail": "priya.personal@example.com", "phone": "+91-9800000000",
  "dateOfBirth": "1995-04-12", "gender": "Female",
  "address": "12, 4th Cross, Bengaluru", "emergencyContactName": "Ramesh Nair",
  "emergencyContactPhone": "+91-9800000001",
  "designation": "Software Engineer", "department": "Engineering",
  "departmentId": "guid", "location": "Bengaluru HQ", "locationId": "guid",
  "managerName": "Anil Kumar", "managerEmployeeId": "guid",
  "joiningDate": "2022-06-13", "employmentType": "FullTime",
  "employmentStatus": "Active", "probationEndDate": "2022-12-12",
  "legalEntity": "Wintech Solutions Pvt Ltd", "costCentre": "ENG-01",
  "avatarUrl": "/api/v1/me/avatar"
}
```

### PUT /me/profile — non-sensitive fields only (FR-EMP-021)
`{ "preferredName", "phone", "personalEmail", "address", "emergencyContactName", "emergencyContactPhone" }`
-> 200 `{ "status": "saved", "message": "Profile updated." }`

### POST /me/sensitive-change-requests — FR-EMP-022 (bank details / legal name / national ID)
`{ "field": "BankAccount", "requestedValue": "...", "reason": "..." }` -> 202 `{ "id", "status": "Pending" }` (routes into approvals; value masked in all responses until verified).

### POST /me/avatar — multipart `file` (jpg/png, <= 2 MB) -> 200 `{ "avatarUrl": "/api/v1/me/avatar" }`; GET /me/avatar -> image bytes (404 if none).

### GET /me/employment-history -> `[{ "eventType", "effectiveDate", "oldValue", "newValue", "reason", "approvedBy" }]` (FR-EMP-025)

### GET /me/notification-preferences -> `[{ "category", "inApp": true, "push": true, "email": false, "mandatory": false }]`
### PUT /me/notification-preferences `{ "items": [ ... ] }` -> 204 (FR-EMP-121; `mandatory: true` items cannot be disabled -> 422).

### GET /me/dashboard — FR-EMP-010..014
```json
{
  "greeting": "Good morning, Priya",
  "today": "2026-10-24",
  "employee": { "employeeNumber": "EMP006", "displayName": "Priya Nair",
    "designation": "Software Engineer", "department": "Engineering",
    "location": "Bengaluru HQ", "managerName": "Anil Kumar", "employmentStatus": "Active" },
  "attendance": {
    "workDate": "2026-10-24", "status": "NotMarked",
    "shift": { "name": "General", "startTime": "09:00", "endTime": "18:00", "graceMinutes": 15 },
    "checkIn": null, "checkOut": null, "workedHours": null,
    "canPunchIn": true, "canPunchOut": false, "lateByMinutes": null
  },
  "leaveBalances": [ { "leaveTypeId": "guid", "code": "CL", "name": "Casual Leave",
    "available": 5.5, "used": 4, "pending": 1, "accrued": 10, "carryForward": 0 } ],
  "pendingApprovals": 2,
  "myRequests": [ { "id": "guid", "type": "Leave", "title": "Casual Leave (2 days)",
    "status": "Pending", "date": "2026-10-23", "amount": null, "deepLink": "/leave/requests/guid" } ],
  "announcements": [ { "id": "guid", "title", "summary", "category", "publishedAt",
    "requiresAcknowledgement": false } ],
  "holidays": [ { "date": "2026-11-01", "name": "Karnataka Rajyotsava" } ],
  "notificationsUnread": 4,
  "quickActions": ["leave.request", "attendance.correct", "payslip.view", "case.create", "documents.view"]
}
```
`attendance.status` enum: `Present Absent WeekOff Holiday Leave HalfDay Late MissingPunch NotMarked`.

---

## 6. Employees & organization — FR-EMP-024, FR-ADM-020..027

### GET /employees/directory — any authenticated (privacy-aware, FR-EMP-024)
`?q=&departmentId=&locationId=&page=&pageSize=&sort=` -> paged:
`[{ "employeeId", "employeeNumber", "displayName", "designation", "department", "location", "workEmail", "hasPhone": false, "avatarUrl" }]`
Phone/personal fields are never exposed in the directory for non-privileged callers.

### GET /employees/{id} — own record, or `employee.view` for others (else 404/403). Same shape as GET /me plus `status`, `managerEmployeeId`. Sensitive fields (bank, national ID) only with `document.sensitive` permission and always masked in responses unless `?unmask=true` + permission (then audited).
### GET /employees/{id}/history — `employee.view`.
### GET /employees — `employee.view` (admin list; filters: `q`, `status`, `departmentId`, `locationId`, paged) -> `{ items: [{ "employeeId","employeeNumber","displayName","workEmail","designation","department","location","status","managerName","joiningDate" }], page, ... }`.
### POST /employees — `employee.create` -> 201 employee detail. Body: `{ "employeeNumber"?, "firstName", "lastName", "workEmail", "phone"?, "joinDate", "designationId", "departmentId", "locationId", "managerId"?, "employmentType", "legalEntityId" }` | 409 duplicate employee number/email.
### PUT /employees/{id} — `employee.edit` (writes employment history for reporting/payroll-relevant fields, FR-ADM-026). Optional `clearPhone`, `clearManager`, and `clearCostCentre` flags explicitly clear those nullable fields.
### POST /employees/{id}/status — `employee.manage`: `{ "status": "Active|Suspended|Terminated", "effectiveDate", "reason" }` -> 200 (FR-ADM-024, BR-008). Terminated users lose access at the effective time.

### GET /org-units -> `[{ "id", "code", "name", "type": "Entity|BusinessUnit|Department|Team", "parentId", "managerName", "status" }]`
### GET /locations -> `[{ "id", "code", "name", "city", "country", "timeZone", "status" }]`
### GET /designations -> `[{ "id", "code", "name", "grade", "status" }]`

---

## 7. Attendance, shifts & holidays — FR-EMP-030..038, FR-ADM-040..046

### GET /attendance?month=YYYY-MM&employeeId=(mine unless `attendance.view.team`) -> paged:
```json
{ "items": [ { "workDate": "2026-10-01", "status": "Present", "shiftName": "General",
    "checkIn": "09:02", "checkOut": "18:11", "workedHours": 8.98, "lateByMinutes": 0,
    "isHalfDay": false, "source": "Mobile", "correctionStatus": null } ],
  "page": 1, "pageSize": 31, "total": 18, "hasNextPage": false,
  "summary": { "present": 12, "absent": 1, "halfDay": 0, "leave": 2, "holidays": 2,
    "weekOffs": 4, "late": 1, "pendingCorrections": 1 } }
```
(summary is part of this response object.)

### GET /attendance/today -> today's record (same item shape) or 200 with `status: "NotMarked"` defaults.
### POST /attendance/punch — FR-EMP-031/032/037
```json
{ "source": "Mobile", "latitude": 12.9716, "longitude": 77.5946, "accuracyMeters": 12 }
```
-> 200 `{ "workDate", "checkIn", "checkOut", "status", "message" }`.
409 on duplicate punch / missing policy window with `{title, detail}`; 422 if geofence required and location missing (only when `Security:GeofenceEnabled`).
### GET /attendance/summary?from=YYYY-MM-DD&to=YYYY-MM-DD -> `{ present, absent, halfDay, leave, holidays, weekOffs, late, overtimeHours, workedHours }`

### POST /attendance-corrections — FR-EMP-035..037
```json
{ "workDate": "2026-10-20", "type": "MissingPunch",
  "proposedCheckIn": "09:05", "proposedCheckOut": "18:00", "reason": "Forgot to check out",
  "attachmentUrl": null }
```
-> 201 `{ "id", "status": "Pending", "approvalTaskId" }` | 409 duplicate open request | 422 outside configured submission window.
### GET /attendance-corrections?status=&month=&employeeId= -> paged `{ id, workDate, type, proposedCheckIn, proposedCheckOut, originalCheckIn, originalCheckOut, reason, status, submittedAt, approverName, approvalTaskId }`
### GET /attendance-corrections/{id} -> detail + `decisionHistory: [{ "action", "actorName", "comment", "at" }]`
(decisions happen via /approvals, see section 9; approved corrections preserve original values — FR-ADM-044, UAT-005.)

### GET /shifts -> `[{ "id", "name", "startTime", "endTime", "graceMinutes", "breakMinutes", "weekDays": ["Mon",...] }]`
### GET /shifts/roster?from=&to= -> `[{ "date", "shiftId", "shiftName", "startTime", "endTime" }]` (FR-EMP-050)
### GET /admin/attendance/dashboard?from=&to=&departmentId=&locationId= -> organization attendance totals and daily/status/leave/department breakdowns (`attendance.manage`).
### GET /admin/attendance/punch-events?from=&to=&employeeId=&departmentId=&locationId=&page=&pageSize= -> captured punch details, filtered by employee and/or organization dimensions (`attendance.manage`).
### POST /shifts/swap-requests — FR-EMP-051 `{ "rosterDate", "proposedWithEmployeeId", "proposedShiftId"?, "reason" }` -> 201 `{id, approvalTaskId}`
### GET /holidays?year=YYYY -> `[{ "date", "name", "type": "Public|Optional|Shutdown", "locationId" }]`

---

## 8. Leave — FR-EMP-040..047, FR-ADM-050..055

### GET /leave-types -> `[{ "id", "code", "name", "isPaid", "requiresAttachment", "allowsHalfDay", "maxConsecutiveDays" }]`
### GET /leave-balances?year=YYYY -> `[{ "leaveTypeId", "code", "name", "available", "accrued", "used", "pending", "carryForward", "unit": "Days" }]` (FR-EMP-040)
### GET /leave-policies -> `[{ "id", "code", "name", "description", "accrualFrequency", "annualEntitlementDays", "carryForwardMaxDays", "negativeBalanceAllowed", "noticeDays", "attachmentRequired" }]`
### POST /leave-requests — FR-EMP-041/042
```json
{ "leaveTypeId": "guid", "startDate": "2026-11-03", "endDate": "2026-11-04",
  "dayType": "Full", "reason": "Family function", "attachmentUrl": null }
```
-> 201 `{ "id", "status": "Pending", "units": 2, "approvalTaskId" }`.
422 with `errors` for: overlapping request, insufficient balance (unless policy allows negative), weekend/holiday-only dates, notice-period/attachment rules. `dayType`: `Full | FirstHalf | SecondHalf`.
### GET /leave-requests?status=&year=&month=&team=true&employeeId= -> paged:
`{ id, leaveType, leaveTypeId, startDate, endDate, units, dayType, status, reason, submittedAt, decidedAt, approverName, rejectionReason, approvalTaskId }`
(`team=true` requires `leave.approve` / `attendance.view.team`.)
### GET /leave-requests/{id} -> detail + `decisionHistory: [{ "step", "action", "actorName", "comment", "at" }]`
### POST /leave-requests/{id}/cancel `{ "reason" }` -> 200 `{ "status": "Cancelled" }` | 409 if already decided or outside the configured window (FR-EMP-044).
### GET /leave/calendar?from=YYYY-MM-DD&to=YYYY-MM-DD -> `[{ "date", "type": "Leave|Holiday|WeekOff", "label", "status" }]` (FR-EMP-045)

Status enum: `Draft Pending Approved Rejected Cancelled CancelledByEmployee` (draft reserved for future).

---

## 9. Approvals (generic engine) — FR-MGR-001..007, FR-ADM-060..065

All module requests (leave, attendance corrections, expenses, travel, letters, profile-sensitive changes, shift swaps) create one **approval task**; decisions are always made here so BR-004 (no self-approval) and BR-007 (reject reason) are enforced centrally.

### GET /approvals/pending — tasks awaiting **me** (also honours delegated assignments)
paged: `[{ "taskId", "requestType", "requestId", "title", "requesterName", "requesterEmployeeNumber", "requestedAt", "stepName", "dueAt", "amount", "currency", "status": "Pending", "deepLink" }]`
`requestType`: `Leave | AttendanceCorrection | Expense | Travel | Letter | ProfileChange | ShiftSwap | Resignation | Other`.
### GET /approvals/mine — my own requests' workflow state: `[{ "taskId", "requestType", "requestId", "title", "status": "Pending|Approved|Rejected|Returned|Cancelled", "currentStep", "approverName", "updatedAt" }]`
### GET /approvals/stats -> `{ "pending", "overdue", "completedThisMonth" }`
### GET /approvals/{taskId} -> `{ taskId, requestType, requestId, title, status, currentStep, dueAt, requesterName, canDecide: true, history: [{ "step", "action": "Approve|Reject|Return|Delegate|AutoEscalate", "actorName", "comment", "at" }] }`
### POST /approvals/{taskId}/decision
```json
{ "action": "Approve", "comment": "Approved." }
```
-> 200 `{ "taskId", "status": "Approved", "completed": true, "message" }` where `completed` means the workflow finished and the underlying domain record was updated by its `IApprovalHandler`.
403 not your task / self-approval attempt (BR-004); 409 already decided; 422 `comment` required when `action` is `Reject` or `Return` (BR-007).
### POST /approvals/{taskId}/delegate `{ "toUserId": "guid", "reason" }` -> 204 (FR-MGR-003; delegation is audited and time-boxed server-side).
### GET /workflows -> admin (`admin.config`): `[{ "id", "code", "name", "version", "isActive", "steps": [{ "stepNumber", "name", "approverType": "Manager|Role|NamedUser|SelfAndManager", "approverValue", "slaDays" }] }]`

---

## 10. Payroll & payslips — FR-EMP-060..064, FR-ADM-070..076

### GET /payslips?year=YYYY -> `[{ "id", "periodId", "periodLabel": "Oct 2026", "startDate", "endDate", "gross", "netPay", "currency", "status": "Published", "publishedAt" }]` — **own records only**; `payslip.view.any` required for another employee (UAT-006: IDs in the URL cannot reach someone else's payslip -> 404).
### GET /payslips/{id} -> `{ id, periodLabel, startDate, endDate, currency, earnings: [{ "component", "amount" }], deductions: [{ "component", "amount" }], gross, totalDeductions, netPay, ytdGross?, employeeNumber, employeeName, legalEntity, generatedAt }`
### GET /payslips/{id}/download -> `200 application/pdf` (generated server-side, authenticated + expiring link semantics, access audited — FR-EMP-062).
### GET /payroll-periods — `payroll.manage`: `[{ "id", "label", "startDate", "endDate", "status": "Open|Locked|Closed", "cutOffDate", "lockedAt" }]`
### POST /payroll-periods/{id}/lock / .../unlock — `payroll.manage` `{ "reason" }` -> 200 (unlock audited, FR-ADM-075).
(Payroll *calculation* is out of scope per BRD R-02: inputs are validated and payslips imported/generated for publishing.)

---

## 11. Documents, letters & policies — FR-EMP-070..074, FR-ADM-080..085

### GET /documents?category=&status=&employeeId=(default mine) -> `[{ "id", "name", "category": "Identity|Education|Employment|Tax|Medical|Other", "documentType", "issuedOn", "expiresOn", "status": "Pending|Verified|Rejected|ExpiringSoon|Expired", "verificationNote", "sizeBytes", "fileExtension", "canDownload", "uploadedAt" }]`
(Another employee's documents require `document.manage`; identity/medical require `document.sensitive` — FR-ADM-084.)
### POST /documents — multipart: `file` + `category` + `documentType` + `issuedOn?` + `expiresOn?` -> 201 document. 400 on type/size violation (allow-list: PDF; common raster images; Word, Excel and PowerPoint formats; OpenDocument and RTF; TXT/Markdown/log; CSV/JSON/XML; EML/MSG; max from config, default 10 MB — BR-010). New uploads are `Pending` verification and malware-quarantined until cleared.
### GET /documents/{id}/download -> `200` file bytes (streamed, never a public URL; access audited).
### POST /documents/{id}/decision — `document.manage` `{ "action": "Verify|Reject", "note" }` -> 200 (FR-ADM-080).
### GET /letters/requests -> mine: `[{ "id", "letterType", "purpose", "status", "createdAt", "approvalTaskId", "generatedDocumentId" }]`
### POST /letters/requests `{ "letterType": "EmploymentCertificate|SalaryCertificate|ExperienceLetter|AddressProof", "purpose", "notes"? }` -> 201 `{ id, approvalTaskId, status }` (FR-EMP-071/072; on approval the PDF is generated from a template with verified data).
### GET /letters/requests/{id}/download -> 200 PDF once generated (409 before approval).
### GET /policies?category=&acknowledgementStatus= -> `[{ "id", "title", "category", "version", "publishedAt", "effectiveFrom", "requiresAcknowledgement", "acknowledgedAt"?, "documentId" }]`
### GET /policies/{id} -> detail incl. `body` (short policy text) or `documentId` for full text.
### POST /policies/{id}/acknowledge `{ "comment"? }` -> 200 `{ "acknowledgedAt", "version" }` — acknowledgement is versioned (FR-EMP-074); 409 if already acknowledged this version.

---

## 12. Expenses & travel — FR-EMP-080..084, FR-ADM-100..103

### POST /expenses (multipart: `receipt` file + fields)
`{ "category", "amount", "currency": "INR", "expenseDate", "businessPurpose", "costCentre"? }` -> 201 `{ id, status, approvalTaskId }`. 400 limit/type violations, 409 duplicate receipt (BR-less, FR-EMP-081).
### GET /expenses?status=&month=&team= -> paged `{ id, category, amount, currency, expenseDate, businessPurpose, costCentre, status, submittedAt, approvedAt, paidReference?, rejectionReason?, approvalTaskId, receiptUrl }` (receipts only for own/privileged).
### GET /expenses/{id} -> detail. POST /expenses/{id}/cancel `{ "reason" }` -> 200/409.
### POST /travel-requests `{ "destination", "purpose", "startDate", "endDate", "estimatedCost", "currency" }` -> 201; GET /travel-requests -> paged (FR-EMP-083).
Status: `Draft Pending ManagerApproved FinanceApproved Approved Rejected Paid Cancelled Returned`.

---

## 13. Assets — FR-EMP-090..093, FR-ADM-090..094

### GET /assets (mine) -> `[{ "id", "assetType": "Laptop|Phone|Badge|Sim|Accessory|Other", "name", "assetTag", "serialNumber", "model", "status": "Assigned|ReturnRequested|Returned", "issuedAt", "condition", "accessories", "acknowledgedAt", "returnDueDate" }]`
### POST /assets/{id}/acknowledge `{ "condition", "note"? }` -> 200 (FR-EMP-091).
### POST /assets/{id}/return-request `{ "reason", "condition" }` -> 200 `{ "status": "ReturnRequested" }` — employees cannot mark an asset returned themselves (FR-EMP-093).
### GET /assets/inventory — `asset.manage`: `{ items: [{ id, assetType, name, assetTag, serialNumber, status, warrantyExpiry, custodianEmployeeName, locationName }], page, ... }` + `overdueReturns` count.
### POST /assets — `asset.manage` `{ assetType, name, assetTag?, serialNumber?, model?, purchaseDate?, warrantyExpiry?, locationId? }` -> 201.
### POST /assets/{id}/assign — `asset.manage` `{ "employeeId", "condition", "accessories"?, "returnDueDate"? }` -> 201 assignment (409 if not `Available`).

---

## 14. Performance & training — FR-EMP-100..104, FR-ADM-110..113

### GET /performance/cycles/current -> `{ id, name, startDate, endDate, status: "Upcoming|Open|Closed", selfAssessment: { status: "NotStarted|Submitted", dueDate, submittedAt? } } | null`
### GET /performance/goals?cycleId= -> `[{ "id", "title", "description", "category", "weight", "target", "progress": 0-100, "status", "managerFeedback"? }]`
### POST /performance/self-assessment `{ "cycleId", "content", "evidence"? }` -> 201 `{ id, status: "Submitted" }` (409 if cycle closed or already submitted; FR-EMP-101).
### GET /performance/reviews -> published reviews `[{ "id", "cycleName", "publishedAt", "rating"?, "summary"?, "acknowledgedAt"? }]`
### POST /performance/reviews/{id}/acknowledge -> 204 (FR-EMP-102; acknowledgement != agreement).
### GET /training/courses?status= -> `[{ "id", "title", "category", "durationHours", "mandatory", "dueDate", "enrollmentStatus": "NotEnrolled|Enrolled|Completed|Overdue", "completedAt"?, "certificateUrl"? }]`
### POST /training/courses/{id}/enroll -> 201 | 409 already enrolled | 422 not eligible (FR-EMP-104).
### POST /training/courses/{id}/complete `{ "score"? }` -> 200 `{ "completedAt" }` — optional courses only; mandatory completions need `training.manage` (FR-EMP-103).

---

## 15. HR helpdesk / employee CRM — FR-EMP-110..115, FR-ADM-120..125

### GET /cases/categories -> `[{ "id", "name", "confidential", "subcategories": [{ "id", "name" }] }]` — categories: Payroll, Leave, Attendance, Benefits, Policy, Onboarding, Workplace, IT, General HR (FR-EMP-111).
### GET /cases?status=&category=&priority=&mine=true&queue=(queues need `case.manage`) -> paged:
`{ id, ticketNumber, subject, category, subcategory, priority: "Low|Medium|High|Urgent", status: "Open|WaitingOnEmployee|WaitingOnHR|Resolved|Closed|Reopened", queue, confidential, createdAt, updatedAt, slaDueAt, firstResponseAt?, resolvedAt?, assigneeName?, employeeName?, rating? }`
### POST /cases `{ categoryId, subcategoryId?, subject, description, urgency, attachmentUrl? }` -> 201 `{ id, ticketNumber, status, slaDueAt }` (FR-EMP-110).
### GET /cases/{id} -> detail + `messages: [{ id, senderName, senderRole, visibility: "Employee"|"Internal", body, createdAt, attachmentUrl? }]` + `canReply`. Confidential cases are 404 for everyone outside the restricted queue (FR-EMP-114, UAT-009); internal messages are omitted for employees (FR-ADM-122).
### POST /cases/{id}/messages `{ body, attachmentUrl? }` -> 201 message (employee replies; FR-EMP-113).
### POST /cases/{id}/notes `{ body }` — `case.manage` -> 201 (internal-only).
### POST /cases/{id}/transition `{ "action": "Resolve|Close|Reopen", "comment"? }` -> 200 (Resolve/Close need `case.manage`; Reopen is the employee within the configured window; resolution code optional for employee).
### POST /cases/{id}/rate `{ "rating": 1-5, "comment"? }` -> 200 (once, after resolve; FR-EMP-113).
### GET /knowledge-base?q=&category= -> `[{ id, title, summary, category, tags, helpfulCount, updatedAt }]` (FR-EMP-115).
### GET /knowledge-base/{id} -> `{ id, title, body, category, version, expiresAt? }`.

---

## 16. Announcements & notifications — FR-EMP-120..124, FR-ADM-130..133

### GET /announcements?category=&page= -> `[{ id, title, summary, body, category: "Company|HR|Policy|Event|IT|Wellness", audience, pinned, publishedAt, expiresAt?, requiresAcknowledgement, acknowledgedAt? }]`
### GET /announcements/{id} -> detail (404 if targeted at another population).
### POST /announcements/{id}/acknowledge -> 204 (409 if not required/already acked).
### POST /announcements — `announcement.create`: `{ title, summary, body, category, audience: "All|Department|Location", audienceIds?: [], requiresAcknowledgement, publishAt?, expiresAt? }` -> 201 (draft -> publish via `POST /announcements/{id}/publish`).
### GET /notifications?unreadOnly=&page= -> `[{ id, type, title, body, deepLink, category, createdAt, readAt }]` (FR-EMP-122). Push payloads never contain sensitive detail (BR-013).
### GET /notifications/unread-count -> `{ "count": 4 }`
### POST /notifications/{id}/read -> 204; POST /notifications/read-all -> 204.
Notifications are created server-side by domain events (leave decision, approval assigned, payslip published, ticket update, document expiring…).

---

## 17. Admin — FR-ADM-140..155

### GET /admin/users?role=&status=&q= — `admin.users`: paged `{ id, employeeId, employeeNumber, displayName, workEmail, roles, status, mfaEnabled, lastLoginAt, sessionsActive }`
### POST /admin/users `{ "employeeId", "roles": [] }` -> 201 | PUT /admin/users/{id}/roles `{ "roles" }` -> 204 (audited; takes effect on the user's next request — UAT-007).
### POST /admin/users/{id}/suspend | /activate `{ "reason"? }` -> 204; POST /admin/users/{id}/revoke-sessions -> 204.
### GET /admin/roles — `admin.roles`: `[{ name, description, isSystem, userCount, permissions: [] }]`
### PUT /admin/roles/{name}/permissions `{ "permissions": [] }` -> 204 (403 on `*`/SuperAdmin edits; every change audited).
### GET /admin/configuration — `admin.config`: `[{ key, value, type: "String|Int|Bool|Date|Json", description, updatedAt, updatedByName }]`
### PUT /admin/configuration/{key} `{ "value" }` -> 200 (BR-012: applies prospectively).
### GET /admin/audit?actor=&action=&entityType=&entityId=&from=&to=&page= — `audit.view`: paged `{ id, actorName, actorEmployeeNumber, action, entityType, entityId, outcome, correlationId, ipAddress, timestamp, detailsJson }`. **Read-only** — audit rows are never mutable (FR-ADM-153).
### GET /admin/operations — `admin.config`: `{ backgroundJobs: [{ name, lastRunAt, status }], notificationFailures24h, integrationErrors24h, dbSizeMb?, uptimeMinutes }` (FR-ADM-004/154).

---

## 18. Reports — section 13 of BRD

All reports: `?from=&to=&departmentId=&locationId=` and include `meta: { generatedAt, generatedBy, filters, scope }`. Permission `report.view` (+`report.export` for export).

### GET /reports/headcount -> `{ groups: [{ key, label, headcount, joiners, exits }], totals: { active, joiners, exits } }`
### GET /reports/approval-aging -> `{ rows: [{ requestType, requesterName, approverName, stepName, ageDays, dueAt, overdue }] }`
### GET /reports/attendance-exceptions -> `{ rows: [{ employeeNumber, employeeName, workDate, exception: "MissingPunch|Late|Absent|PendingCorrection", detail }] }`
### GET /reports/document-expiry -> `{ rows: [{ employeeNumber, employeeName, documentType, expiresOn, daysToExpiry, status }] }`
### GET /reports/leave-liability -> `{ rows: [{ department, leaveType, entitlement, used, pending, carryForward }] }`

### GET /reports/{report}/export?format=csv|xlsx|pdf&from=&to=&departmentId=&locationId= -> downloaded report file
Requires `report.export`; exports the same filtered rows as CSV, Excel `.xlsx`, or PDF. Each export is audited with the actor, format, filters and row count.

---

## 19. Health / meta

- `GET /health` -> 200 `{ "status": "Healthy", "database": "Up", "time", "service": "wintech-hrms-api" }` (anonymous)
- `GET /` -> service banner with docs link (anonymous)
- `GET /swagger` -> OpenAPI UI (anonymous, Development)

---

## 20. Error semantics quick reference for the mobile app

- 401 anywhere -> clear stored tokens, return to login.
- 403 "Account not active" -> show blocking screen with reason, force logout.
- 422 -> show `errors` field map inline under form fields; `title`/`detail` as banner.
- 409 -> toast/banner `detail` (e.g. duplicate punch); refresh the affected screen.
- 429 -> show "Too many attempts, try in N seconds" using `Retry-After`.

---

## 21. Resignation & exit — FR-EMP-130..133

One active resignation per employee; the workflow runs through the generic engine with `requestType: "Resignation"`.

### POST /resignations
`{ "lastWorkingDay": "YYYY-MM-DD", "reasonCategory": "BetterOpportunity|HigherEducation|Relocation|Personal|Health|Family|Other", "reason": "...", "comments"?: "..." }` -> 201 `{ "id", "status": "Pending", "approvalTaskId", "submittedAt", "noticePeriodDays" }`
- 409 when an active (Pending/Approved) resignation already exists (FR-EMP-130).
- 422 when `lastWorkingDay` violates the notice-period policy read from configuration (`Resignation.NoticeDays`, default 30) — `errors.lastWorkingDay`.
- 400 when `lastWorkingDay` is not in the future.

### GET /resignations?status= — own records only
`[{ "id", "status": "Pending|Approved|Rejected|Cancelled|Withdrawn", "submittedAt", "lastWorkingDay", "lastWorkingDayLabel", "reasonCategory", "reason", "approvalTaskId", "currentApproverName", "relievingDate"?, "checklistProgress": { "total", "completed", "overdue" } }]`

### GET /resignations/{id} — own record (others: `employee.manage`)
detail of the list item + `decisionHistory: [{ "step", "action", "actorName", "comment", "at" }]` + `exitChecklist: [{ "id", "task", "owner": "Employee|Manager|HR|IT|Payroll", "status": "NotStarted|InProgress|Completed|Waived", "dueDate", "completedAt", "note" }]`

### POST /resignations/{id}/withdraw `{ "reason" }` -> 200 `{ "status": "Withdrawn" }` | 409 once decided (FR-EMP-130 withdrawal policy).

### GET /resignations/{id}/checklist — FR-EMP-131/132
`[{ "id", "task": "KnowledgeHandover|AssetReturn|AccessRevocation|ExitSurvey|DocumentAcknowledgement|FullAndFinal", "owner", "status", "dueDate", "completedAt", "note" }]`

### POST /resignations/{id}/checklist/{itemId}/complete `{ "note"?, "attachmentUrl"? }` -> 200 checklist item | 409 when already completed or not assigned to the caller.

### POST /resignations/{id}/exit-survey `{ "overallRating": 1-5, "recommendScore": 1-10, "likes": "...", "improvements": "...", "wouldRejoin": true }` -> 201 | 409 when submitted before (FR-EMP-132).
The survey is only available once the resignation is Approved; before that -> 409.

Access after exit (FR-EMP-133) is governed by `Access.PostExitDays` (configuration) and the account status rules in section 6 — no extra endpoint.

---

## 22. Onboarding & offboarding checklists — FR-ADM-025, FR-ADM-031..035

Checklists are materialised per employee. Reading the list is self-healing: employees hired within
the last 90 days without an onboarding checklist get one created automatically from the default
template (FR-ADM-025), so no separate "trigger" call is required.

### GET /lifecycle/checklists?type=Onboarding|Offboarding&status=&q= — `employee.view`, paged
`{ items: [{ "id", "type": "Onboarding|Offboarding", "employeeId", "employeeNumber", "employeeName", "designation", "managerName", "stage": "PreBoarding|Day1|Week1|Completed|Exit", "progressPercent", "startDate", "dueDate", "overdueCount", "tasksTotal", "tasksCompleted" }], page, pageSize, total, hasNextPage }`

### POST /lifecycle/checklists — `employee.create` `{ "employeeId", "type": "Onboarding|Offboarding", "templateCode"?, "startDate"? }` -> 201 checklist | 409 when an equivalent active checklist already exists.

### GET /lifecycle/checklists/{id} — `employee.view`
`{ ...list item..., "tasks": [{ "id", "code", "title", "description", "owner": "Employee|Manager|HR|IT|Facilities|Payroll", "mandatory": true, "status": "NotStarted|InProgress|Completed|Waived|Overdue", "dueDate", "completedAt", "completedBy"?, "note"?, "order" }], "auditTrail": [{ "action", "actorName", "at", "note" }] }`

### POST /lifecycle/checklists/{id}/tasks/{taskId} — `employee.manage` `{ "action": "Start|Complete|Reopen|Waive", "note"? }` -> 200 task | 409 illegal transition (FR-ADM-035 tracks exceptions; every transition is audited).

### GET /lifecycle/templates — `employee.create` -> `[{ "code", "name", "type", "active", "taskCount", "tasks": [{ "code", "title", "owner", "mandatory", "dueOffsetDays" }] }]`
