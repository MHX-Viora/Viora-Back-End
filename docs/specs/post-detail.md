# Spec: Post Detail API

## Objective

Add authenticated `GET /api/posts/{postId}` for opening a published post or short video from feed, notifications, shares, and profiles.

## Contract

- Return only the requested response fields, including viewer reaction/save/ownership state, author, media, and hashtags.
- Return `404` when the post is missing, deleted, or soft-deleted.
- Return `403` when the post exists but is not published or its visibility denies the current user.
- Public posts and owner posts are visible; follower-only posts require an active follow; private posts are owner-only.
- Infer media type from post type because the current schema stores media under a post without a separate media type column.

## Implementation

- ASP.NET Core controller obtains `user_id` from the access token and sends a MediatR query.
- Application contracts define an explicit response DTO and result errors.
- EF Core repository uses `AsNoTracking` and DTO projection; query count is fixed and does not depend on media or hashtag count.
- Swagger derives response schemas from `ProducesResponseType` attributes.

## Verification

- `dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --filter PostFeedApiContractTests -p:NuGetAudit=false`
- `dotnet build viora-BE/viora-BE.csproj -p:NuGetAudit=false`

## Boundaries

- Do not add dependencies or database migrations.
- Do not change feed or interaction response contracts.
- Do not return draft, hidden, deleted, or unauthorized post data.

## Success Criteria

- Route, response DTO, `403`, and `404` are documented in OpenAPI.
- Both regular posts and short videos use the same endpoint.
- Viewer state and nested collections are projected without N+1 queries.
