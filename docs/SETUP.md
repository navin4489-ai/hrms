# Wintech HRMS — local setup

End-to-end instructions for running the API (.NET 9) and the mobile app (Flutter) that make up the
Wintech HRMS solution described in the BRD.

## Repository layout

| Folder | Contents |
|---|---|
| `docs/` | Business + technical documentation. `API_CONTRACT.md` is the **single source of truth** for every endpoint, payload and error shape. |
| `api/` | .NET 9 Web API (`Wintech.Hrms.Api`), SQL Server/PostgreSQL in production, SQLite for local development, InMemory fallback for tests. |
| `mobile/` | Flutter app (Android + iOS) built against `docs/API_CONTRACT.md`. |

## Prerequisites

- .NET SDK 9.0+
- Flutter 3.47+ (`flutter --version`)
- For production runs: Microsoft SQL Server or PostgreSQL

## 1. Run the API

```powershell
cd api
dotnet build
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run
```

Development defaults (`appsettings.Development.json`):

- URL: `http://localhost:5080` (override with `ASPNETCORE_URLS`)
- Database: SQLite file `api/App_Data/wintech-hrms.dev.db`, created and seeded on first start.
  Delete the file to re-create the schema and re-run the seeders.
- Swagger UI: `http://localhost:5080/swagger`
- Health: `GET http://localhost:5080/health`

Production (SQL Server or PostgreSQL): set the environment to `Production`, configure
`Database__Provider` as `SqlServer` or `PostgreSql`, and provide a matching connection string:

```
Database__Provider = PostgreSql
Database__ConnectionString = Host=...;Database=...;Username=...;Password=...;SSL Mode=Require
```

### Seeded demo users

All demo accounts use the password **`Passw0rd!`** (company: *Wintech Solutions Pvt Ltd*):

| Username | Role | Typical use |
|---|---|---|
| `admin` | SuperAdmin | Everything, including admin & audit screens |
| `hr` | HRExecutive | Employee records, documents, HR cases |
| `hrexec` | HRHead | HR-wide access, dashboards, restricted cases |
| `manager` | LineManager | Approvals for direct reports |
| `payroll` | PayrollFinance | Payroll periods, payslips, reimbursements |
| `employee` | Employee | Self-service (attendance, leave, payslips, cases) |
| `employee2` | Employee | Second employee (peer/team views) |
| `auditor` | Auditor | Read-only audit log and reports |

```powershell
Invoke-RestMethod -Uri 'http://localhost:5080/api/v1/auth/login' -Method Post `
  -ContentType 'application/json' -Body '{"username":"admin","password":"Passw0rd!"}'
```

### Error envelope

Every non-2xx response is:

```json
{ "status": 404, "title": "Not found", "detail": null, "errors": null, "correlationId": "…" }
```

`422` responses carry a field map in `errors`, `401` anywhere means "clear tokens, back to login",
`429` carries `Retry-After` (see `docs/API_CONTRACT.md` §20).

## 2. Run the mobile app

```powershell
cd mobile
flutter pub get
flutter analyze
flutter test
flutter run          # emulator or device with the API reachable
```

The API base URL is injected at compile time via `--dart-define`
(`mobile/lib/core/config/env.dart`, default `http://localhost:5080`):

```powershell
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5080   # Android emulator
flutter run --dart-define=API_BASE_URL=http://localhost:5080   # iOS simulator / desktop
```

## 3. Verify a fresh checkout

```powershell
cd api      && dotnet build          # 0 errors
cd mobile   && flutter analyze       # no issues found
            && flutter test          # all tests pass
```

## 4. Deploy the API to Render

Root-level `render.yaml` defines the API and Render Postgres database. To deploy it:

1. Push this project to a GitHub repository and connect that repository in Render.
2. In Render, create a **Blueprint** for the repository; Render finds root-level `render.yaml`.
3. The Blueprint provisions Render Postgres and wires its internal connection string to the API.
4. Render generates `Jwt__SigningKey` and `Security__SeedDemoPassword` for the service. Use the generated seed password
   for the initial accounts; `Passw0rd!` is Development-only.
5. Wait for `GET https://<render-service>.onrender.com/health` to report `Healthy`.

Both the API Web Service and database use Render **Free** plans. Free Postgres expires after 30 days;
free web services may sleep when idle. The service has no persistent disk, so uploaded files are
ephemeral and may disappear on restart. This is for a demo only; do not store important HR data.
Choose paid plans and persistent storage for ongoing use. Keep the service private during initial setup,
retrieve the generated bootstrap password from Render, and rotate or disable demo accounts before
exposing the service URL. Do not use SQLite for production.

Build the Android release APK with the deployed API URL (without `/api/v1`):

```powershell
cd mobile
flutter build apk --release `
  --dart-define=API_BASE_URL=https://<render-service>.onrender.com
```

APK output: `mobile/build/app/outputs/flutter-apk/app-release.apk`.
