# Spec: Authenticated User Profile

## Objective

Create and partially update the authenticated account's profile, including server-managed Cloudinary images.

## API Contract

- `POST /api/accounts/login`: active JSON responses contain `status`, `accessToken`, and nullable `user`; refresh tokens use an HttpOnly cookie.
- `POST /api/users/profile`: consumes `multipart/form-data` with required `displayName` and `gender`, plus optional `avatar` and `cover` image files.
- `PATCH /api/users/profile`: consumes `multipart/form-data`; any combination of `displayName`, `gender`, `avatar`, and `cover` may be supplied, but an empty patch is invalid.
- There are no separate PATCH routes for avatar or cover.
- Clients cannot submit `avatarUrl` or `coverUrl`. The API validates each image (JPEG, PNG, or WebP; maximum 5 MB), uploads it to Cloudinary, validates the returned HTTPS URL, and then persists that URL.
- Omitted PATCH fields preserve their existing values.
- Successful create/update returns the user object directly: `id`, `accountId`, profile fields, `role`, `isVerified`, and `verificationStatus`.
- Duplicate create, missing profile, unavailable account, or invalid token returns a Vietnamese message with a suitable HTTP status.

## Architecture and Security

- The account ID comes only from the validated JWT `sub` claim, never from request data.
- JWT bearer validation verifies HS256 signature, issuer, audience, lifetime, and `token_type=access`.
- Application owns profile rules/contracts; Infrastructure owns EF persistence; API owns authentication and HTTP mapping.
- Only active accounts may create/update profiles.
- Cloudinary assets use deterministic public IDs: `viora/users/{accountId}/profile/avatar` and `viora/users/{accountId}/profile/cover`, so later uploads replace the prior image.
- No database migration is required because `Users.AccountId` is already unique.

## Testing

- xUnit covers multipart contracts, removal of image-specific routes, image validation, upload URL persistence, partial updates, duplicate/missing profile, and inactive accounts.
- Build: `dotnet build viora-BE.sln --no-restore -m:1 -c Release`.
- Test: `dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --no-build --no-restore -c Release`.

## Boundaries

- Always: authorize both endpoints, validate ownership from JWT, validate files before upload, and accept only Cloudinary-returned HTTPS image URLs.
- Ask first: changing profile ownership or adding new database columns.
- Never: accept `accountId`, role, verification flags, or status from the client.

## Success Criteria

Invalid/missing bearer tokens receive 401; exactly one POST and one PATCH profile operation appear in OpenAPI; create/update persist Cloudinary URLs; partial updates preserve omitted fields; all tests/build pass.

## Implementation Plan

1. Define multipart API and application contracts; prove them with failing reflection/service tests.
2. Implement create with optional image uploads and one database save.
3. Implement the unified partial update and remove image-specific PATCH actions/contracts.
4. Run focused tests, full tests, build, dead-code search, and security/correctness review.
