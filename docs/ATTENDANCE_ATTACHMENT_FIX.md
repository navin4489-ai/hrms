# Attendance and chat attachments — October 26, 2026

## Changes

- Checkout now attaches the no-tracking daily record before modifying it. Previously an event could save without updating the daily checkout time.
- Startup creates `att_punch_events` and its indexes on legacy databases. DDL errors fail startup instead of being hidden.
- The mobile punch flow refreshes capture settings per punch, opens the preferred **front** camera before GPS, and allows a punch when camera/location permission is declined. An explicit admin photo-off setting is still respected.
- Successful punches refresh the home card and attendance providers.
- New chat attachment bytes are stored privately in `chat_attachment_content`, in the same database transaction as the message. They no longer depend on Render's ephemeral filesystem. Downloads still require active conversation membership.
- Files have separate Open and Save actions; Save uses the system destination picker. Images open a zoomable viewer with a Save action. Lost legacy uploads show an explicit resend message.

## Deployment

Deploy the updated API so startup creates the attendance and durable attachment tables. Install APK version **1.0.1 (build 2)** for camera ordering and attachment Save changes.

Existing chat metadata is preserved. Bytes already lost from an earlier ephemeral filesystem cannot be reconstructed; those attachments must be resent (or restored from a file backup). Existing files still on disk use the legacy download fallback.

Database storage consumption will now include chat attachments (up to the existing 25 MB per-file limit). Monitor capacity, include attachment data in database backups, and define retention before high-volume use. Attendance selfies and other legacy document files still use the configured private file store and require persistent storage for long-term retention.

## Repeatable verification

```powershell
dotnet run --project tests/AttendanceRegression/AttendanceRegression.csproj
# Run from mobile/:
flutter test
```

The backend regression uses an isolated in-memory SQLite database, simulates missing tables, advances a fake clock for checkout, and verifies image/PDF downloads after replacing the private file store. It never modifies production attendance or sends messages to real employees. The mobile tests verify front-camera ordering and image-viewer/error behavior. PostgreSQL deployment and physical phone camera/save-dialog behavior need post-deploy/device verification; compilation alone does not establish those outcomes.
