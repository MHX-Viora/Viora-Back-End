# Post Detail API Handoff

Implemented authenticated `GET /api/posts/{postId}` through `PostsController`, MediatR, and `PostFeedRepository`.

- Missing, deleted, or soft-deleted posts return `404`.
- Draft/hidden posts and visibility failures return `403`.
- Public, follower-only, private, and owner access follow existing post visibility rules.
- Response includes viewer reaction/save/ownership state, author, media, and hashtags.
- Both regular posts and short videos are supported; media type is inferred from post type because the database has no media type column.
- EF Core uses `AsNoTracking` projections and a fixed query count with no collection N+1 behavior.

Verification:

- API project builds with zero warnings/errors.
- `PostFeedApiContractTests`: 7 passed.
