# Community post content limit

- Objective: accept community posts up to 50,000 UTF-16 code units, matching existing .NET and React Native length semantics; reject 50,001.
- Scope: app composer, create-post command validator and multipart form validation. Reel captions and comments retain their existing limits.
- Storage: Posts.Content has no configured maximum; retain its existing text column without a migration.
- Plan: verify storage; check command and multipart boundaries; update backend limits, then app input; review focused diffs.
- Style: retain existing MaximumLength / MaxLength / maxLength conventions.
- Verification: `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --filter FullyQualifiedName~CreatePostContentLimitTests`; app: `npx eslint components/feed/create-post-modal.tsx`.
- Boundaries: no dependencies, API shape changes, deployment, or unrelated edits.
- Result: command and multipart validation pass all three boundary cases (49,999 / 50,000 / 50,001); composer ESLint and focused diff checks pass. Changes require backend deployment and app update to reach users.
