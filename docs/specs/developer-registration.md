# Developer registration completion

## Objective and acceptance
Use the existing signed-in ANKT account, never a client-selected owner. Registration creates a Pending profile and owner membership. Active developers can create apps; app publication still requires domain proof and separate moderation.

Rejected owners can correct and resubmit with the existing profile PUT: successful validation returns the same profile to Pending and records DeveloperResubmitted. Pending/Active edits preserve status. Suspended owners and members cannot edit. Failed validation never resubmits.

Admin transitions: Pending -> Active/Rejected; Active -> Suspended; Suspended -> Active. Rejected profiles must be resubmitted before approval. Audit actions accurately describe decisions. No status change grants an admin role to a developer.

## Stack, structure and conventions
.NET 8/EF Core service in Viora.Infrastructure/MiniApps; xUnit SQLite integration tests in Viora.Application.Tests. Expo/React Native in ../viora/features/developer; React portal in ../viora-admin/src/features/developer. Reuse existing service methods, Vietnamese copy and UI components; localized diffs, no dependency or schema changes.

## Plan and verification
1. Add regression tests for resubmission, invalid decisions, invalid profiles and ownership; reproduce failures, then fix service state/validation/audit. Verify with dotnet test Viora.Application.Tests --filter FullyQualifiedName~DeveloperRegistrationTests.
2. Update app and portal resubmission copy, validation and inactive states; hide app/team operations until Active. Verify existing Node tests, TypeScript, lint and portal build; browser walkthrough if available.
3. Run mini-app integration regression tests and review changes; record evidence/limits in docs/handoffs/developer-registration.md.

## Boundaries
Always: preserve authenticated ownership, admin-only moderation, immutable mini-app review and existing worktree changes. No production deployment, database migration, new dependencies or unrelated refactoring. Scope is completion of the existing onboarding flow, not a second identity system.
