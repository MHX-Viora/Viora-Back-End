# Spec: ANKT Mini App Platform v1

## Objective

Replace the native, hard-coded AntiFake integration with a backend-managed Mini App platform. An authenticated ANKT user discovers active Mini Apps, grants approved permissions, receives a short-lived one-time launch code, and opens the partner site in one shared secured WebView. Admins manage developers, apps, permissions, lifecycle, and audit data without rebuilding the mobile app.

## Tech Stack

- ASP.NET Core 8 Web API, EF Core 8, PostgreSQL, JWT
- Expo 54 / React Native 0.81 / Expo Router / `react-native-webview`
- React 19 / Vite admin using the existing Axios and React Query stack

## Commands

- Backend build: `dotnet build viora-BE.sln`
- Backend test: `dotnet test viora-BE.sln --no-build`
- Migration: `dotnet ef database update --project Viora.Infrastructure --startup-project viora-BE`
- Mobile test: `npm test`
- Mobile lint: `npm run lint`
- Admin build: `npm run build`
- Admin lint: `npm run lint`

## Project Structure

- `Viora.Domain/Entities`: Mini App persistence model and centralized enums
- `Viora.Application/MiniApps`: contracts, policies, service interfaces
- `Viora.Infrastructure/MiniApps`: EF-backed services and security primitives
- `viora-BE/Controllers`: public, auth, admin, developer, and partner endpoints
- `viora/features/mini-apps`: dynamic center, consent, WebView, bridge
- `viora-admin/src/pages`: Mini App and Developer management
- `docs/MINI_APP_INTEGRATION.md`: partner integration guide

## Code Style

```csharp
public sealed record LaunchMiniAppResponse(
    bool RequiresConsent,
    IReadOnlyList<MiniAppPermissionDto> Permissions,
    string? LaunchUrl,
    int? ExpiresIn);
```

Use typed DTOs, async cancellation, thin controllers, EF parameterization, centralized status/error constants, and existing project naming conventions. Never return EF entities from APIs.

## Testing Strategy

- Unit-test URL allowlisting, bridge validation, permission projection, launch-code hashing/expiry, and secret verification.
- Integration-oriented service tests cover status/ownership/consent and atomic code consumption where the existing test infrastructure permits.
- Build all three projects and run mobile logic tests after each vertical slice.

## Boundaries

- Always: validate external input, authorize ownership/roles, hash secrets and launch codes, enforce HTTPS in production, redact credentials and codes from logs.
- Approved by this spec: new auth flow, external integration, PostgreSQL migration, WebView dependency, and rate-limit policies.
- Never: expose ANKT JWT, account id, email, client secret, or raw launch code to unapproved consumers; hard-code AntiFake or any partner in mobile; let developers self-activate apps.

## Success Criteria

- Existing AntiFake routes/features/services/types are removed.
- Mobile list is sourced only from `GET /api/mini-apps`; one generic WebView handles every app.
- Consent gates only admin-approved permissions; launch codes are random, hashed, 30–60 seconds, bound to app/account, and atomically one-time.
- Exchange authenticates the partner and projects only consented profile fields with a stable per-app subject.
- WebView blocks unsafe schemes and non-allowlisted hosts; bridge methods are schema- and permission-checked.
- Admin can review, approve/reject, suspend/reactivate, edit app domains/permissions, and manage developers.
- Migration, Swagger surfaces, SDK, partner docs, builds, tests, and lint are complete.

## Open Questions

- Developer Portal UI is deferred; v1 exposes developer APIs. Developer identity uses the authenticated account mapped to `Developer.AccountId`.
- No legacy AntiFake data is migrated because the current integration is client-only and hard-coded.
