# User Profile Handoff

## Endpoints

- `POST /api/users/profile` consumes multipart fields `displayName`, `gender`, optional `avatar`, and optional `cover`; it returns HTTP 201.
- `PATCH /api/users/profile` consumes the same multipart fields, updates only supplied values, and returns HTTP 200; an empty request returns HTTP 400.
- There are no separate avatar/cover PATCH routes and clients cannot submit image URLs.
- Both require `Authorization: Bearer <accessToken>`; `accountId` comes from the validated JWT `sub` claim.
- Images accept JPEG, PNG, or WebP up to 5 MB each. The server uploads them to Cloudinary and persists only returned HTTPS URLs.

Business failures return `{ "message": "..." }` in Vietnamese: duplicate create=409, missing profile=404, inactive account=403, invalid profile=400. Missing/invalid token returns 401.

## Login Change

`POST /api/accounts/login` returns `accessToken` in JSON and sets refresh token as an HttpOnly cookie; profile authorization still accepts access tokens only.

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
