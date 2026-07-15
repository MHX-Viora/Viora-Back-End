# Social Relationships Handoff

## Endpoints

- `POST /api/users/{userId}/follow` toggles follow and returns `{ isFollowing, followerCount }`.
- `GET /api/users/{userId}/relationship` returns the viewer relationship state for follow/friend/message buttons.
- `GET /api/users/me/statistics` returns `{ postCount, followerCount, followingCount, friendCount }` for the authenticated profile page.
- `GET /api/users/{userId}/profile` returns another user's public profile fields, counts, follow state, friendship state, messaging permission, and existing private `conversationId`.
- `POST /api/friends/request` accepts `{ userId }` and creates a pending friendship request.
- `GET /api/friends/requests` lists incoming pending requests from `Friendships`.
- `POST /api/friends/{friendshipId}/accept` accepts a request, creates a private conversation, adds both members, and returns `{ conversationId }`.
- `POST /api/friends/{friendshipId}/reject` rejects a request and sends `FriendRejected`.
- `DELETE /api/friends/{id}` supports both cancellation and unfriend because the requested cancel/unfriend routes have the same HTTP shape. Pending friendship id cancels a sent request; accepted friend user id cancels the friendship. Conversations are preserved.

All endpoints require Bearer access tokens and use the `user_id` claim.

Statistics count only `Posts.Status = Published`; post count includes both regular posts and short videos because both live in `Posts`.
`canMessage` is true when the target allows messages from everyone or the users have an accepted friendship. Existing private conversations are returned but never created by profile reads.

## Implementation

- Contracts, validators, handlers: `Viora.Application/Social`.
- EF Core repository: `Viora.Infrastructure/Persistence/Repositories/SocialRepository.cs`.
- Controllers: `FriendsController`, `UserRelationshipsController`.
- `NotificationType.FriendRejected = 20` was added.

## Verification

```powershell
dotnet build viora-BE/viora-BE.csproj --no-restore
dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --filter SocialApiContractTests
```
