# Wintech HRMS — Mobile App (Android & iOS)

Flutter client for the Wintech HRMS employee self-service portal and the
employee-facing CRM (helpdesk), built against
[`docs/API_CONTRACT.md`](../docs/API_CONTRACT.md).

- **Employee self-service (FR-EMP-*)** — login with MFA, dashboard with punch,
  attendance + corrections, leave, approvals, directory, profile, payslips,
  documents/policies/letters, expenses & travel, assets, performance &
  training, helpdesk/FAQ, announcements, notifications and preferences.
- **Architecture** — Riverpod (state/DI), go_router (shell navigation + deep
  links), Dio (networking), Material 3 design system.
- **Quality gates** — `flutter analyze` → 0 issues, `flutter test` → all green.

---

## 1. Requirements

| Tool   | Version tested |
|--------|----------------|
| Flutter | 3.47.x (Dart 3.13.x) |
| Android Studio / Xcode | any version able to run the Flutter 3.47 toolchain |
| API    | `docs/api` running on `http://localhost:5080` |

The app itself is plain Flutter — no platform tooling beyond the normal
`flutter run` requirements is needed.

## 2. Getting started

```powershell
cd mobile
flutter pub get

# Host machine runs both the API and the app (simulator / desktop):
flutter run --dart-define=API_BASE_URL=http://localhost:5080

# Android emulator (the emulator's alias for the host machine):
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5080
```

`API_BASE_URL` is read once in `lib/core/config/env.dart`
(`String.fromEnvironment`) and always gets `/api/v1` appended. It defaults to
`http://localhost:5080`, so the plain `flutter run --dart-define=...` command
above works without any extra configuration.

### Demo credentials

All seeded users use the password **`Passw0rd!`** (see `docs/API_CONTRACT.md`
§2). The sign-in screen has a *Use demo credentials* button that fills
`employee` / `Passw0rd!`.

### Platform notes for local (HTTP) development

- **Android** — `android:usesCleartextTraffic="true"` and the `INTERNET`
  permission are set in `android/app/src/main/AndroidManifest.xml`.
- **iOS** — `NSAppTransportSecurity → NSAllowsArbitraryLoads` is set in
  `ios/Runner/Info.plist` (development only; remove before shipping).
- **Downloads** are written to the app's documents directory
  (`lib/core/utils/file_saver.dart`); the resulting path is shown in a snackbar.

## 3. Project structure

```
mobile/
├── lib/
│   ├── main.dart                 # ProviderScope + bootstrap
│   ├── app.dart                  # MaterialApp.router + theme
│   ├── core/
│   │   ├── config/env.dart       # API_BASE_URL dart-define
│   │   ├── network/              # ApiClient (Dio) + ApiException
│   │   ├── storage/              # TokenStorage (secure storage + fallback)
│   │   ├── strings.dart          # every user-facing string
│   │   └── utils/                # formatters, validators, file_saver
│   ├── models/                   # DTO classes (fromJson/toJson)
│   ├── providers/                # Riverpod providers per API domain
│   ├── router/                   # go_router config + deep-link parser
│   ├── theme/                    # Material 3 theme
│   ├── widgets/                  # shared UI kit (see §5)
│   └── features/                 # screens, one folder per domain
│       ├── auth/  dashboard/  attendance/  leave/  approvals/
│       ├── directory/  profile/  documents/  expenses/  assets/
│       ├── performance/  helpdesk/  announcements/  notifications/
│       ├── settings/  more/  shell/
├── test/                         # model, form and auth-logic tests
├── README.md                     # this file
└── INTEGRATION_NOTES.md          # contract gaps, deps, integration TODOs
```

## 4. Runtime architecture

- **Session** — `authControllerProvider` restores the stored session on cold
  start (`GET /auth/refresh` → `GET /me`), then the router redirects based on
  `AuthStatus` (`restoring | unauthenticated | mfaRequired | blocked |
  authenticated`).
- **Networking** — a single `ApiClient` attaches `Authorization: Bearer …` and
  a UUIDv4 `X-Correlation-Id` to every request, converts every failure into
  `ApiException` (status/title/detail/`errors`/correlationId), and performs a
  **single-flight refresh on 401**: concurrent 401s share one refresh call and
  each request is retried exactly once. A failed refresh clears the session and
  fires `onSessionExpired` → logout.
- **Validation** — 400/422 `errors` maps are surfaced inline per field
  (`ServerFieldErrors` mixin in `widgets/form_error.dart`), with a banner
  fallback for non-field errors.
- **Navigation** — `StatefulShellRoute.indexedStack` with five tabs
  (Home / Attendance / Leave / Approvals / More); detail screens are nested
  routes, so back behaviour and tab state behave natively. API `deepLink`
  values are translated by `router/deep_link_parser.dart` (unknown links fall
  back to the dashboard).
- **Data** — Riverpod `FutureProvider`s own server state; screens invalidate
  after mutations and support pull-to-refresh.

## 5. Shared UI kit (`lib/widgets/`)

| Widget | Purpose |
|---|---|
| `AsyncBody` | loading → error → empty → data state machine |
| `StatusChip` / `statusVisual` | status → colour + **icon + text** (never colour-only) |
| `AppButton`, `MiniActionButton` | primary/tonal/outlined/danger buttons with busy state |
| `SectionCard`, `InfoRow`, `CardDivider` | consistent detail sections |
| `EmptyState`, `EmptyStateAction` | friendly empty lists with a CTA |
| `ErrorBanner`, `FormErrorBanner` | request-level / form-level errors |
| `ConfirmDialog` | destructive confirmations (cancel, reject, revoke) |
| `DateRangeField` | start/end date picker pair with ordering validation |
| `ShimmerBox` | skeleton placeholders |
| `AppScaffold` + `NotificationsBell` | standard app bar with unread badge |
| `ServerFieldErrors` (mixin) | maps API `errors` onto form fields |

Conventions: all copy lives in `lib/core/strings.dart`, dates/times render in
**device local time** (`core/utils/formatters.dart`), every touch target is
≥44 px, icon-only buttons carry tooltips/semantic labels, and every screen
with a list supports pull-to-refresh.

## 6. Testing

```powershell
flutter analyze   # 0 issues
flutter test      # model, login-form and auth-logic suites
```

| Suite | Covers |
|---|---|
| `test/models_test.dart` | `fromJson`/`toJson` for announcements, notifications, preferences, knowledge-base DTOs |
| `test/login_form_test.dart` | client-side validation, demo fill, password visibility, busy/error states — no network |
| `test/api_client_auth_test.dart` | bearer + correlation-id headers, single-flight refresh under concurrency, forced logout on refresh failure, 422 envelope → `ApiException.errors` |

The auth suite injects a fake Dio `HttpClientAdapter`, so it runs fully
offline.

## 7. Accessibility & UX commitments

- Minimum touch target 48 px, 4.5:1 text contrast, no colour-only meaning.
- Loading, empty, error, offline and permission-denied states everywhere.
- Permission-gated UI (quick actions, approval decisions, sensitive fields) —
  the server remains the authority; a 403 shows a friendly explanation.
- Localised-ready strings: every literal is centralised in `Strings`.

See [`INTEGRATION_NOTES.md`](INTEGRATION_NOTES.md) for contract gaps,
additional dependencies and integration follow-ups.
