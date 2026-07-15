# Friends API Handoff

## Scope

Implemented and updated friends APIs:

- `GET /api/friends`
- `POST /api/friends/request`
- `PUT /api/friends/{friendshipId}/accept`
- `PUT /api/friends/{friendshipId}/reject`

## Behavior

- `GET /api/friends` supports `page`, `pageSize`, `status`, and `keyword`.
- Allowed list statuses are `Pending`, `Accepted`, and `Rejected`.
- Pending and rejected lists only return relationships where the current user is `AddresseeUserId`.
- Accepted returns all friendships involving the current user.
- List response returns the other user's summary and `mutualFriendCount`.
- Friend request creation handles existing `Pending`, `Accepted`, `Blocked`, `Cancelled`, and `Rejected` relationships.
- Rejected/cancelled relationships are reused and set back to `Pending`.
- Accept updates friendship, creates a `FriendAccepted` notification, and creates a private conversation only when one does not already exist.
- Reject only updates friendship to `Rejected`; it does not create a notification.
- Friendship, notification, and conversation writes are done inside repository transactions.

## Verification

- `dotnet build Viora.Application/Viora.Application.csproj --no-restore -v minimal` passed.
- `dotnet build viora-BE/viora-BE.csproj --no-restore -v minimal -o ./artifacts/tmp-api-build` passed.
- `dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --no-restore --filter SocialApiContractTests -v minimal -o ./artifacts/tmp-test-build` passed.
- Normal API build output can fail while a running `viora-BE` process locks DLLs under `viora-BE/bin`.
