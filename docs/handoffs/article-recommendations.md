# Handoff: Article recommendations

## Found from the interrupted session

- Backend already contained the recommendation endpoint/service, normalized five-signal ranking, cold-start behavior, author/content affinity, discovery boost, deterministic server pagination, per-page diversity, and not-interested filtering.
- Interaction tracking already used one row per user/article/type, monotonic read progress, bounded validation, access checks, and first-view deduplication.
- Migration `20260908023051_AddArticleRecommendations` already created `ArticleInteractions` with foreign keys, checks, a unique anti-spam constraint, and query indexes.
- Client already contained Recommended/Latest/Trending tabs, Recommended default, loading/infinite scroll, ID deduplication, impression/open/read/not-interested tracking, and responsive layouts.
- Remaining confirmed gap: selecting the Article category did not update route params, so browser refresh returned to Community.

## Completed in this continuation

- Persisted Article and Community category selection in Expo Router params while preserving local immediate state changes.
- Added the refresh-state regression test (observed RED before implementation, then GREEN).
- Added cold-start score and recommendation pagination-normalization tests.
- Added the approved continuation spec and implementation plan.

## Database

- Migration: `20260908023051_AddArticleRecommendations`.
- Purpose: store idempotent article interactions and monotonic reading progress.
- EF reports no model changes after the migration. The migration was not applied to or rolled back against a live database in this session.

## API

- `GET /api/articles/recommended?page=&pageSize=&keyword=` returns an authenticated, server-ranked `PostFeedResponse`.
- `POST /api/articles/{id}/interactions` records allowed client signals; user identity comes from the authenticated claim.
- `GET /api/articles/{id}` now increments view count/history only for the user's first deduplicated view.

## Ranking

`FinalScore = Interest × 0.40 + Engagement × 0.25 + Freshness × 0.20 + AuthorAffinity × 0.10 + Discovery × 0.05`.

Engagement is exponentially normalized so raw views cannot dominate. Freshness decays with a 48-hour half-life. Interest uses hashtag and strong-read history; author affinity uses follow and prior server-side interaction signals; discovery boosts low-view articles. A viewed-item penalty, not-interested exclusion, stable tie-breakers, and diversity reordering are applied.

## Verification

- Backend Application tests: PASS (46/46).
- Focused Article tests: PASS (27/27).
- Backend build: PASS (0 warnings, 0 errors).
- Frontend tests: PASS (167/167).
- TypeScript: PASS.
- Lint: PASS.
- Expo web export: PASS.
- EF migration/model consistency: PASS; one unrelated pre-existing `Report.Reason` sentinel warning remains.
- Production dependency audit: PASS (0 vulnerabilities).
- Live authenticated Recommendation API and browser click-through: NOT RUN; no browser instance/session was available and no live test identity/database was supplied.
