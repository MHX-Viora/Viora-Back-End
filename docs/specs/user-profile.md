# Spec: Authenticated User Profile

## Objective

Remove refresh tokens from login responses and add authenticated APIs to create and update the current account's `User` profile.

## API Contract

- `POST /api/accounts/login`: unchanged status behavior, but active responses contain only `status`, `accessToken`, and nullable `user`.
- `POST /api/users/profile`: requires `Authorization: Bearer <accessToken>` and creates the profile for the JWT account.
- `PUT /api/users/profile`: requires the same header and updates the existing profile.
- Profile body: `displayName` (required, max 100), optional HTTPS/HTTP `avatarUrl` and `coverUrl` (max 2048), and `gender` (`0..2`).
- Successful create/update returns the user object directly: `id`, `accountId`, profile fields, `role`, `isVerified`, and `verificationStatus`.
- Duplicate create, missing profile, unavailable account, or invalid token returns a Vietnamese message with a suitable HTTP status.

## Architecture and Security

- The account ID comes only from the validated JWT `sub` claim, never from request data.
- JWT bearer validation verifies HS256 signature, issuer, audience, lifetime, and `token_type=access`.
- Application owns profile rules/contracts; Infrastructure owns EF persistence; API owns authentication and HTTP mapping.
- Only active accounts may create/update profiles.
- No database migration is required because `Users.AccountId` is already unique.

## Testing

- xUnit covers create/update success, duplicate/missing profile, inactive account, response mapping, access-only login token, and request validation metadata.
- Build: `dotnet build viora-BE.sln --no-restore -m:1 -c Release`.
- Test: `dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --no-build --no-restore -c Release`.

## Boundaries

- Always: authorize both profile endpoints, validate ownership from JWT, return Vietnamese business errors.
- Ask first: changing profile ownership or adding new database columns.
- Never: accept `accountId`, role, verification flags, or status from the client.

## Success Criteria

Login has no refresh token, invalid/missing bearer tokens receive 401, create/update return the requested user shape, and all tests/build pass.
