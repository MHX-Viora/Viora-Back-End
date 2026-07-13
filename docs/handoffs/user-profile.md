# User Profile Handoff

## Endpoints

- `POST /api/users/profile` creates the current account's profile and returns HTTP 201 with the user object.
- `PUT /api/users/profile` replaces editable profile fields and returns HTTP 200 with the user object.
- Both require `Authorization: Bearer <accessToken>`; `accountId` comes from the validated JWT `sub` claim.
- Body: `displayName`, nullable `avatarUrl`, nullable `coverUrl`, and numeric `gender` (`0=Unknown`, `1=Male`, `2=Female`).

Business failures return `{ "message": "..." }` in Vietnamese: duplicate create=409, missing profile=404, inactive account=403, invalid profile=400. Missing/invalid token returns 401.

## Login Change

`POST /api/accounts/login` now returns only `accessToken`; refresh-token generation and response fields were removed.

## Security

- JWT bearer validation checks HS256 signature, issuer, audience, lifetime, and `token_type=access`.
- Swagger exposes Bearer authorization.
- Profile role and verification fields are server-owned and cannot be updated by the request.
- Console/Debug logging is used to avoid Windows Event Log permission failures in JWT challenge handling.

## Verification

```powershell
dotnet build viora-BE.sln --no-restore -m:1 -c Release
dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --no-build --no-restore -c Release
```

No database migration is required.
