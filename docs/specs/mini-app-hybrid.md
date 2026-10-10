# ANKT Hybrid Mini App Platform

## Objective and authorization

Implement the user's supplied 12-phase hybrid platform requirements in the existing ANKT backend, Expo client and React admin. The supplied request authorizes schema changes, WebView dependency, partner authentication, developer portal, tests and documentation. No production deployment or database update is authorized. Preserve unrelated changes and existing ANKT login/social/wallet/chat/live/notifications.

## Decisions

- Extend existing Mini App services/entities/controllers; no parallel platform. Preserve existing `Active` persisted status as approved and published; introduce archive/version review without changing historical enum values. Existing apps default to `AnktSso`; new wizard defaults to `Independent`.
- Developer management is available at `/developer` inside the Expo ANKT app using the current ordinary user session; the existing admin web app additionally hosts a separately authenticated Developer Portal. Existing admin login remains restricted to admins. API ownership/membership checks are authoritative.
- ANKT provides pairwise identity. Partners own account creation, linking, unlinking and sessions. Provide executable ASP.NET Core and Node integration examples with authenticated linking, explicit confirmation and conflict checks. Never link by matching email.
- Launch starts the registered website and creates a short-lived runtime session. SSO is an explicit SDK request with exact registered redirect URI, partner-generated state and S256 PKCE. ANKT access tokens, runtime bearer secrets and client secrets never enter embedded pages.
- Backend WebView session validation binds account/app/session and current status. Page bridge additionally binds app ID, session ID and a fresh nonce, validates payload/origin/source, limits methods/rate/size and expires on navigation/closure.
- Domains are verified before app approval via bounded public HTTPS challenge with DNS/IP validation, redirects disabled and request limits. No arbitrary website proxy. Sensitive published edits require review with an immutable version snapshot.
- Native uses React Native WebView; web uses sandboxed iframe with source/origin checks and an always-available external-tab fallback. Camera/microphone and unmanaged downloads are not auto-granted. System-browser login is available for provider flows that prohibit embedding.

## Shared API contract

Retain existing mini-app and consent endpoint roots. Extend existing DTOs additively.

Public list/detail include `developer`, `categoryId`, `category`, `authenticationMode` (`Independent`/`AnktSso`), `publishedVersion`. Detail includes `webUrl`, `allowedDomains`, `allowedOrigins`, `callbackUrls`, approved `permissions`.

- `GET /api/mini-apps/categories`: active category DTOs `{id,name,slug}`.
- `POST /api/mini-apps/{id}/launch`: `{launchUrl,allowedDomains,sessionId,sessionToken,expiresIn,permissions,requiresConsent}`. Session token is host-private; initial launch returns no authorization code.
- `POST /api/mini-apps/{id}/sessions/validate`: `{sessionId,sessionToken}` => `{active,permissions,expiresAt}`; permissions are currently granted codes, not all requested codes.
- `POST /api/mini-apps/{id}/authorize`: `{sessionId,sessionToken,redirectUri,state,codeChallenge,codeChallengeMethod:'S256'}` => existing launch response with `launchUrl` including code/state, or `requiresConsent:true` with missing permissions.
- `POST /api/mini-app-auth/exchange`: `{clientId,clientSecret?,code,redirectUri,state,codeVerifier}`. Client auth method is `ClientSecretPost` or `None`; PKCE required for all newly issued codes.
- Existing consent grants use `{permissions:string[],granted:boolean}`. Existing revoke route removes grants; session validation thereafter reflects revocation.
- Developer roots `/api/developer/profile`, `/api/developer/mini-apps`, `/api/developer/team`; app routes `submit-review`, `rotate-secret`, `domains`, `versions`, `analytics`, `audit`.
- Domain DTO `{id,host,verifiedAt,challengeToken}`; add domain `{host}` and verify `/{domainId}/verify` with `.well-known/ankt-mini-app-verification.txt` proof. Team mutations are owner-only.
- Categories `/api/admin/mini-app-categories`; existing admin permissions root gains management. Existing review routes accept a `reason` and review the pending version. Reports and usage use dedicated scoped DTOs/endpoints.

Configuration extends existing input with `categoryId`, `authenticationMode`, `callbackUrls`, `allowedOrigins`, `clientAuthenticationMethod`; maintain existing singular `callbackUrl` for migration and clients. `publishedVersion` and nullable `pendingVersion` are integer revision numbers assigned by review submission. Standard mode does not require a callback or identity scopes. Published views project approved version settings, not unreviewed edits.

## Structure/style

Backend: existing MiniApps folders, EF configurations/migrations and `Viora.Application.Tests`. Client: `features/mini-apps`, `services/mini-app.service.ts`, `types/mini-app.ts`, `app/mini-apps`. Admin: existing Mini App pages plus developer portal and feature styles. Thin typed controllers, EF transactions, structured errors and existing design tokens.

Example contract: `public sealed record ValidateMiniAppSessionRequest(Guid SessionId, string SessionToken);`

## Verification

- Backend: `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false` and `dotnet build viora-BE.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false`.
- Migration: generate with installed EF tool, inspect additive SQL, validate pending-model check; do not apply to production.
- Admin: `npm run build`, `npm run lint`.
- Client: `node node_modules/typescript/bin/tsc --noEmit`, `npm run lint`, focused Node tests for bridge/URL policy. Expo dependency installed with version-compatible Expo tooling.
- Regression tests cover developer/app ownership, review/version states, safe URLs/domains, grants/revoke, code expiry/single-use/wrong app/redirect/state/PKCE, runtime suspension and bridge validation. PostgreSQL concurrency test is conditional on an explicit test connection.
- Browser verification when an available browser connects. Device and production readiness are separately reported and never inferred from builds.

## Boundaries

Follow-up clarified: the personal profile settings entry opens `/developer` INSIDE the Expo ANKT app, using its ordinary user session. It supports Developer self-registration/status/profile, owned Mini App creation/editing/domain proof/review/SSO integration/history/analytics. It must not open the admin application or require admin credentials/a separately configured portal URL. Existing admin moderation remains separate.

Always: additive migration, hashed credentials/codes/session secrets, HTTPS, SSRF defenses, explicit consent, origin/source validation, minimal diffs outside Mini App scope. Never: production mutations, shared passwords, ANKT token injection, iframe-policy bypass, mock data replacing API, secrets in source/logs, silent email linking.
