# Community Post Feed Handoff

## Scope

`GET /api/posts?page=1&pageSize=10&keyword=...` returns authenticated community feed data through MediatR query handling.

## Implementation

- API: `viora-BE/Controllers/PostsController.cs`
- Query/handler/contracts: `Viora.Application/Posts/*`
- Repository: `Viora.Infrastructure/Persistence/Repositories/PostFeedRepository.cs`
- DI: `Program.cs` registers MediatR; `DependencyInjection.cs` registers `IPostFeedRepository`.

## Behavior

- Requires bearer access token.
- Viewer user id is read from JWT `user_id`; accounts without a profile fall back to public-visible feed.
- Filters posts to `Published` and `DeletedAt == null`.
- Visibility:
  - `Public`: visible.
  - `Followers`: visible to author or followers.
  - `Private`: visible to author only.
- Keyword searches user display name, post content, and location.
- Feed ordering uses deterministic score, not random and not created-at-only.
- Cold start uses interaction counts plus freshness and stable tie-breakers.
- Empty post links are returned as `null`.
- Shared posts include `originalPost` with the original author's core post data and media.

## Interaction APIs

- `POST /api/posts/{postId}/reactions`
- `POST /api/posts/{postId}/comments`
- `POST /api/comments/{commentId}/replies`
- `POST /api/posts/{postId}/save`
- `POST /api/posts/{postId}/share`
- `DELETE /api/posts/{postId}`
- `POST /api/posts/{postId}/report`

Implemented through `Viora.Application/Posts/PostInteraction*` and `PostInteractionRepository`.

## Verification

- `dotnet build viora-BE/viora-BE.csproj -p:OutputPath=C:\Users\minec\viora-BE\.build-check\ --nologo`
- `dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj -p:OutputPath=C:\Users\minec\viora-BE\.test-check\ --nologo --filter "AccountAuthCookieTests|AccountCrudTests|PostFeedApiContractTests"`
