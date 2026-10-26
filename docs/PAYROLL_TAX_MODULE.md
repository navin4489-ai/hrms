# Payroll & Tax Module (automatic income tax)

Finance/admin maintains each employee's **pay details** once; the system then
**computes income tax (TDS)**, statutory deductions and full payslips from those
details. Tax is never typed in by hand.

## Who can use it

| Capability | Permission |
| --- | --- |
| View/edit pay details, preview tax, generate payslips, manage periods | `payroll.manage` (PayrollFinance role, admin roles) |
| Publish payslips to employees | `payroll.publish` |
| View own published payslips | signed-in employee (self-service, unchanged) |

The **Payroll** org module must be enabled (default ON; SuperAdmin can toggle it
per organization). The mobile app hides the tiles when disabled.

## Workflow

1. **More → Salary & tax** — finance opens an employee and fills in the annual
   pay components: Basic, HRA, DA, special allowance, other allowance, plus
   employer PF/gratuity (CTC information), monthly professional tax and the tax
   regime (New default / Old). For the Old regime, Chapter VI-A declarations
   (80C, 80D, home-loan interest, other) can be entered.
2. The screen shows a **live tax preview** (debounced `POST .../preview-tax`
   against unsaved values): annual gross → standard deduction → taxable income →
   slab tax → 87A rebate → 4% cess → annual tax and monthly TDS.
3. **More → Payroll periods → (calculator icon)** — *Generate payslips* creates
   draft payslips for the open period from every employee's pay details:
   - Earnings: Basic, HRA, DA, special/other allowance (annual ÷ 12, prorated by
     the share of calendar months the period covers — full month = 1.0).
   - Deductions: employee PF (12% of monthly basic, ₹15,000 wage ceiling →
     max ₹1,800), employee ESI (0.75% when monthly gross ≤ ₹21,000), state
     professional tax, and **Income Tax (TDS)** = annual tax ÷ 12.
   - Employees without pay details or already holding a payslip for the period
     are skipped (reported back). Regeneration is refused while every payslip
     exists (409) — no duplicates.
4. **Publish icon** (permission `payroll.publish`, reason required) releases the
   drafts; employees then see them under Payslips with the TDS line and PDF.

## Tax rules (Indian, financial year April–March)

- **New regime** — FY 2024-25 slabs; FY 2025-26 onward (incl. FY 2026-27)
  Budget-2025 slabs, ₹75,000 standard deduction, §87A rebate up to ₹12L taxable
  (₹60,000 cap).
- **Old regime** — classic slabs, ₹50,000 standard deduction, Chapter VI-A
  deductions (80C/80D/home-loan/other as declared), §87A up to ₹5L (₹12,500).
- 4% health & education cess on tax after rebate. The calculator
  (`api/Modules/Payroll/IncomeTaxCalculator.cs`) is pure and covered by
  regression tests; estimates are recomputed on every read, never stored.

## API

| Method & path | Purpose |
| --- | --- |
| `GET /api/v1/payroll/salaries?q=` | All active employees + pay details + computed tax |
| `PUT /api/v1/payroll/salaries/{employeeId}` | Create/replace pay details (audited) |
| `POST /api/v1/payroll/salaries/preview-tax` | Tax from unsaved values (nothing persisted) |
| `POST /api/v1/payroll-periods/{id}/payslips/generate` | Auto-calculate payslips for a period |
| `POST /api/v1/payroll-periods/{id}/payslips/publish` | Publish drafts (reason required) |

## Database

New table `pay_employee_salary_structures` (one row per employee, unique index
on `EmployeeId`). On databases created before this feature,
`PayrollSchemaCompatibility.EnsureAsync` creates it idempotently at startup
(PostgreSQL / SQLite / SQL Server), wired into `TenantSchemaCompatibility`.
A DDL failure aborts startup rather than serving broken payroll endpoints.

**Deploy note:** Render must use *Manual Deploy → Clear build cache & deploy*
(the Dockerfile `BUILD_REV` token also busts the layer cache).
