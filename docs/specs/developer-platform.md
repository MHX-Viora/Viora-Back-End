# Developer Platform implementation specification

## Objective and authorized boundaries
Implement the supplied Developer Platform prompt incrementally on the existing .NET8/EF/PostgreSQL, Expo and Admin repositories. Reuse Developer, Membership, MiniApp, Version, Domain, Permission, Consent, Audit, Notifications and OpenIddict. Existing working tree changes are preserved. Additive migrations and SMTP adapter are authorized by the request; never apply production migrations, DNS/TLS/deploy or commit here.

## Decisions
- Existing Developer.Status Pending/Active/Suspended/Rejected numeric values remain compatible; add Draft/NeedMoreInformation/Revoked. Public registrationStatus translates PendingReview/Approved. Immutable DeveloperApplications snapshots + DeveloperReviewHistory preserve submitted versions. Pending initial records cannot edit; rejected/need-information may create a new draft. Approved profile updates require a new review, preserving approved registry fields until acceptance.
- Email OTP required for new submissions: 10 minute expiry, account+email binding, hashed code with protected server key, 5 attempts, resend cooldown, never returned/logged. SMTP delivery must be configured; missing configuration returns explicit 503. No legal documents collected unless a documented verification policy and private storage exist; contact verification is not business legitimacy.
- Versioned terms supplied by API, acceptedTermsVersion + acceptedAt + account stored on submission. Phone verification is not mandatory in this release. Legal organization fields required by type.
- Owner/Admin/Developer/Analyst/Viewer permissions enforced server-side; legacy Member maps to Developer. Secret rotation needs recent auth; ownership transfer requires a separate future MFA workflow and is not silently permitted.
- App metadata is part of immutable configuration snapshot. Explicit new review decision separates approval/publication; old published snapshots continue serving while new drafts await review. New limited technical tester never issues production OAuth test tokens or claims sandbox isolation.
- Notifications use the existing persisted notification model; no tokens or private notes in public notifications.

## API contracts (authoritative for parallel UI work)
Existing GET /api/developer/profile returns enriched profile. GET /api/developer/terms returns {version,documents:[{id,title,content}],emailVerificationRequired:true,phoneVerificationRequired:false,legalDocumentsRequired:false}.
POST /api/developer/application/draft uses DeveloperInput fields name,companyName,email,phone,website,accountId plus partnerType (Individual/Business/Organization),legalName,tradingName,businessRegistrationNumber,country,address,representativeName,version(optional expected integer). Returns enriched profile. POST /api/developer/application/submit {version,termsVersion,acceptedTerms:true} submits immutable snapshot.
POST /api/developer/email-verification/request {email} returns {verificationId,expiresAt,resendAfterSeconds}; POST /api/developer/email-verification/confirm {verificationId,code} returns {verified:true,email,verifiedAt}. GET /profile includes contactEmailVerified using current verification.
Enriched profile: existing fields + partnerType,legalName,tradingName,businessRegistrationNumber,country,address,representativeName,registrationStatus,registrationVersion,submittedAt,publicReason,contactEmailVerified,termsAcceptedVersion,termsAcceptedAt,reviewHistory [{id,reviewerId,previousStatus,status,reason,createdAt,version}]. No internal notes. Existing POST/PUT profile cannot bypass new verification/terms workflow; registration endpoints instruct new wizard or save draft, never directly activate.
GET /api/admin/developers/{id}/application returns same enriched profile plus reviewHistory internalNotes and current draft/pending details. POST /{id}/review {version,status,reason,internalNotes}; status Approved/Rejected/NeedMoreInformation/Suspended/Revoked. Reason required for all adverse decisions. Legacy approve/reject/suspend endpoints call same guarded review service with version/reason body.
MiniApp tester/review/publish/team contracts finalized by backend lifecycle owner in MiniAppContracts.cs and shared to frontends.

## Ordered slices and verification
1. Developer entities/DTOs + draft/email/terms/submit/review with deterministic SQLite tests for IDOR, pending immutability, OTP expiry/replay, reviewer version/race, notes separation, account restrictions.
2. Team roles and MiniApp metadata/tester/approval-publication endpoints with business/security tests; additive migration generated only after both model slices stabilize.
3. Expo/Admin registration wizard/status/dashboard/review/team/app tools against actual contracts; TypeScript, targeted lint/tests, build and browser checks.
4. Full .NET regression incl existing OAuth/PKCE/token boundary suites, SDK regression, migration model check and generated SQL review; update handoff/deployment/manual requirements.

## Commands and structure
BE: dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -nr:false; dotnet build viora-BE/viora-BE.csproj -c Release --no-restore -m:1 -nr:false.
Admin: npm run build; npx eslint <changed files>. Expo: npx tsc --noEmit; node --test <focused tests>; npx expo export --platform web.
Domain entities in Viora.Domain/Entities, contracts in Viora.Application/MiniApps, EF and services in Viora.Infrastructure, controllers in viora-BE/Controllers. Follow existing partial service/record DTO/style and explicit MiniAppException codes. No unrelated redesign or abstractions.

## Acceptance / limits
Approved owner controls only own organization; unapproved cannot create production apps; submitted snapshots immutable; rejection/changes require reason and actor/version/time audit; new scopes/callback/domain await review; published snapshot never draft; suspended/revoked block runtime and OAuth. Real SMTP/infrastructure and PostgreSQL migration staging/native device checks require operator environment; do not report these as completed by local tests.

## Verification checkpoint — 2026-10-12
- Additive AppDbContext migration `20261012012833_AddDeveloperPlatformLifecycle` and idempotent SQL generated; existing registrations use Legacy, old release/authentication metadata is backfilled. No production migration applied.
- Existing fixture regressions updated to execute required technical tests; no approval, consent, ownership or publication guards removed.
- Readable UTF-8 terms/notifications/mail, registration validation and Light/Dark portal support verified locally. Complete source/verification/operator handoff: workspace `docs/handoffs/developer-platform.md`.
