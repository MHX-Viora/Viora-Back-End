# Spec: Article recommendations

## Objective

Continue the existing article recommendation implementation without replacing its architecture. The article feed exposes Recommended, Latest, and Trending modes; Recommended is the default and ranks server-side from normalized interest, engagement, freshness, author affinity, and discovery signals.

## Tech Stack

- Backend: .NET 8, ASP.NET Core, MediatR, EF Core, PostgreSQL.
- Client: Expo 54, React Native 0.81, TypeScript.

## Commands

- Backend build: `dotnet build viora-BE.sln --no-restore --disable-build-servers -m:1 --nologo`
- Backend tests: `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore --disable-build-servers -m:1 --nologo`
- Client tests: `npm test`
- Client type check: `npx tsc --noEmit`
- Client lint: `npm run lint`

## Project Structure

- `Viora.Application/Articles`: recommendation, interaction contracts, validation, and handlers.
- `Viora.Infrastructure/Persistence`: EF configuration, repositories, and migration.
- `viora-BE/Controllers/ArticlesController.cs`: HTTP endpoints.
- `../viora/features/feed`: article feed state and presentation.
- `../viora/features/article`: reader and reading-progress tracking.
- `../viora/services`: client API calls.

## Code Style

Follow existing records, MediatR handlers, repository interfaces, functional React components, local state, and theme tokens. Extend contracts additively and preserve existing endpoint response shapes.

## Testing Strategy

- Unit-test normalized scoring, diversity, validation, reading milestones, and navigation state.
- Build both projects and run the complete client suite plus focused backend Article tests.
- Verify migration/model consistency without resetting or deleting database data.

## Boundaries

- Always: preserve current uncommitted work, paginate on the backend, deduplicate client pages, validate tracking input, and keep ranking scores bounded.
- Ask first: destructive database operations, new dependencies, authentication changes, or unrelated architecture changes.
- Never: reset/revert/stash current work, duplicate services/endpoints/migrations, redesign the page, or use AI/LLM ranking.

## Success Criteria

- Article tabs are ordered `Đề xuất | Mới nhất | Xu hướng`; Recommended is selected by default.
- Refreshing the Article feed preserves the Article category and resets its sort to Recommended.
- New users receive a non-empty freshness/engagement/discovery feed when published articles exist.
- Interaction writes are monotonic/idempotent and reading progress is sent only at bounded milestones/final exit.
- Recommendation results are server-ranked, paginated, deterministic, and diversified.
- Backend/client tests, builds, type checking, lint, and migration consistency pass.

## Assumptions

- The current product requires authentication before entering the feed; guest recommendation is therefore out of scope until that existing authentication policy changes.
- Existing article/category data is sufficient; no new category model is required.
- The uncommitted implementation is the approved continuation baseline.

## Open Questions

None blocking.
