# Create Post Handoff

## Scope

`POST /api/posts` creates an authenticated user's post from `multipart/form-data`.

## Request

- Auth: Bearer access token with `user_id` claim.
- Form fields:
  - `post`: JSON string with `content`, `visibility`, `latitude`, `longitude`, `locationName`, `link`.
  - `files`: optional images.

The controller never accepts client-supplied user ids.

## Implementation

- Controller: `viora-BE/Controllers/PostsController.cs`
- Command/handler/validator/contracts: `Viora.Application/Posts/*`
- Storage: `Viora.Infrastructure/Media/CloudinaryMediaStorage.cs`
- Persistence: `PostRepository`, `EfUnitOfWork`
- Migration: `20260714054429_AddPostCoordinates`

`locationName` maps to existing `Posts.Location`. `Latitude` and `Longitude` are new nullable columns on `Posts`.

## Validation

- Content max 5000 chars.
- Requires content or at least one image.
- Max 10 files.
- Accepts only `image/jpeg`, `image/png`, `image/webp`.
- Rejects video by content type.

## Logout Note

`POST /api/accounts/logout` now uses the Bearer token account id. If a refresh cookie is present, it revokes that session; if no cookie is sent, it revokes all active refresh tokens for that account.

## Verification

```powershell
dotnet test Viora.Infrastructure.Tests\Viora.Infrastructure.Tests.csproj -p:OutputPath=C:\Users\minec\viora-BE\.test-check\ --nologo --filter "AccountAuthCookieTests|AccountCrudTests|PostFeedApiContractTests|CreatePostValidatorTests"
```
