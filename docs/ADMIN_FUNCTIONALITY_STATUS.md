# BRD Admin Functionality Coverage

This is the working coverage ledger for `Wintech_HRMS_BRD.docx`, section 6 (FR-ADM-001–155).
`Implemented` means a mobile workflow and API behavior exist; it does not assert external-provider
certification or business acceptance. `Partial` means a useful subset exists, with the gaps listed.

| BRD IDs | Status | Current mobile/API coverage and remaining scope |
|---|---|---|
| ADM-001–004 | Partial | Workforce dashboard, reports and operations snapshot exist. Missing the complete filtered KPI dashboard, entity/cost-centre/employment-type scopes, full authorized drill-downs and robust integration/job delivery metrics. |
| ADM-010 | Partial | Admin can maintain entity/business-unit/department/team, location, designation/grade, leave types, shifts and holidays. Cost-centre catalogue, employment-type catalogue and work-calendar administration are not implemented. |
| ADM-011–014 | Partial | Manager assignment, effective-dated employee history, audit trail and generic configuration editing exist. Dotted-line hierarchy, effective delegation, reference-data history UI, attendance rule configuration and complete branding/localization controls remain. |
| ADM-020–027 | Partial | Employee create/view/edit/status and CSV preview/import with row errors are available; key employee edits create history. Full master fields, identity matching, mandatory-field rules, complete lifecycle events and an atomic/audited batch-import job are not. |
| ADM-030–035 | Partial | Onboarding/offboarding checklists, task transitions, employee documents, account creation and asset assignment exist. Pre-boarding/ATS handoff, provider-backed verification, checklist template editing and onboarding notification orchestration remain. |
| ADM-040–046 | Partial | Shift catalogue, holidays, attendance views/corrections and approval routing exist. Attendance-source imports/reconciliation, period lock/reopen, overtime rules and full attendance policy/approval-chain configuration remain. |
| ADM-050–055 | Partial | Leave-type/policy CRUD, leave approval and holiday CRUD exist. Eligibility assignment criteria, accrual/carry-forward jobs and preview, balance-adjustment workflow, and full matrix/delegation/SLA rules remain. |
| ADM-060–065 | Partial | Versioned sequential approval workflow editing, delegation actions and an approval queue exist. Parallel stages, rule conditions (amount/entity/location/department), escalation/reminders and stuck/unassigned analytics remain. |
| ADM-070–076 | Partial | Period lock/reopen, server-validated payslip import, draft review and separate publish permission exist. Period setup/cutoff editing, compensation structures, input completeness across attendance/leave/claims, payroll-engine export/reconciliation and business-approved segregation remain. |
| ADM-080–085 | Partial | Employee document verification, private download controls, policies/acknowledgements and letters exist. Document-type/retention configuration, policy/template publishing/versioning and archival/legal-hold workflows remain. |
| ADM-090–094 | Partial | Inventory, asset creation/allocation, return/transfer/repair/lost/damaged/disposal and overdue counts exist. Reconciliation/exit-clearance reporting and richer event history UI remain. |
| ADM-100–103 | Partial | Expense/travel submission and approval-backed review exist. Policy limits/receipt configuration, finance disposition/payment reference, booking/settlement and finance export/reconciliation remain. |
| ADM-110–113 | Partial | Performance-cycle and course-catalogue CRUD exist. Review templates/competencies/rating scales/eligibility, calibration, completion dashboards, enrollment administration and certificate evidence remain. |
| ADM-120–125 | Partial | Case assignment, transitions, internal notes, knowledge-base read and basic case lists exist. Case form/category/SLA/queue configuration, merge/duplicate history, operational metrics and knowledge-base authoring/review remain. |
| ADM-130–133 | Partial | Announcement draft/publish/schedule, all/department/location audience and acknowledgement flag exist. Entity/employment-type targeting, retire/withdraw, acknowledgement reporting and email/SMS/push channel management remain. |
| ADM-140–144 | Partial | User account creation, employee linking, role/permission updates, suspension, session revocation and MFA endpoints exist. Temporary role grants/approvals, privileged-role MFA enforcement evidence and break-glass controls/alerts remain. |
| ADM-150–155 | Partial | Audited report CSV export, audit log read, operations snapshot and background-job/notification summaries exist. XLSX/PDF, export preview/masking/approval, searchable audit filters, retention/archive and verified backup/restore controls remain. |

## External decisions / validation required

- Payroll calculation and export format/provider (BRD R-02).
- Attendance source/device provider and reconciliation contract (R-03).
- Legal entity/country rules, statutory and bank-field requirements, retention durations, and access scopes (R-01/R-07).
- Notification, document storage/scanning, digital-signature and identity-provider choices (R-09/R-10).
- Approved support/backup ownership and RPO/RTO, including a witnessed restore test (R-11).

Do not mark full BRD admin acceptance complete until every `Partial` item is either implemented and
tested or explicitly deferred/accepted by the business owner.
