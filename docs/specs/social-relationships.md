# Spec: Social Relationships

## Objective

Add authenticated follow and friendship APIs without changing existing account, profile, post, feed, hashtag, or interaction routes.

## API Contract

- `POST /api/users/{userId}/follow` toggles following. Follow creates a `Follows` row and a `Follow` notification; unfollow removes the row. Response: `isFollowing`, `followerCount`.
- `POST /api/friends/request` accepts `{ "userId": "uuid" }`, rejects self requests, existing friends, and pending requests, then creates a pending `Friendship` and `FriendRequest` notification.
- `GET /api/friends/requests` reads pending incoming requests from `Friendships`, not `Notifications`, and returns requester `id`, `displayName`, `avatarUrl`, `isVerified`, and request `createdAt`.
- `POST /api/friends/{friendshipId}/accept` is allowed only for the addressee. It marks the friendship accepted, sets `RespondedAt`, notifies requester, creates a private conversation, adds both users as members, and returns `conversationId`.
- `POST /api/friends/{friendshipId}/reject` is allowed only for the addressee. It marks the friendship rejected, sets `RespondedAt`, and sends `FriendRejected`.
- `DELETE /api/friends/{id}` handles both delete contracts because `DELETE /api/friends/{friendshipId}` and `DELETE /api/friends/{friendId}` are the same route shape. If `id` is a pending friendship id, requester cancellation sets `Cancelled`; otherwise it treats `id` as a friend user id and cancels an accepted friendship without deleting conversations.
- `GET /api/users/{userId}/relationship` returns `isFollowing`, `friendStatus`, `isRequester`, `canMessage`, and `conversationId`.
- `GET /api/users/me/statistics` returns the current user's `postCount`, `followerCount`, `followingCount`, and accepted `friendCount`.
- `GET /api/users/{userId}/profile` returns another user's profile fields, the same statistics, current viewer follow/friendship state, message permission, and existing private `conversationId`.

## Structure and Style

- Application owns CQRS contracts, validators, handlers, result mapping, and persistence abstractions under `Viora.Application/Social`.
- Infrastructure owns LINQ/EF Core persistence in `SocialRepository`.
- API project owns JWT user extraction and HTTP status mapping.
- Existing APIs are additive-only; no existing route templates are changed.
- Profile read models use DTO projection only and count published posts/videos through `Posts.Status = Published`.

## Testing

Run focused tests:

```powershell
dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --filter SocialApiContractTests
```

## Boundaries

- Always: use current user from JWT `user_id`, validate self-action rules, use `Friendships` as source of truth for friend requests, keep old conversations after unfriend, and return only the documented profile/statistics fields.
- Ask first: schema changes or deleting friendship rows instead of status transitions.
- Never: infer friend requests from notifications or create duplicate pending/accepted relationships.

## Success Criteria

Routes are authenticated, business rules match the contract, private conversations are created on accept, and the solution builds with focused social tests passing.
