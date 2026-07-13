# Account Login Handoff

## API

- `POST /api/accounts/login` accepts `{ identifier, password }`; identifier may be an email or phone number.
- Invalid credentials return HTTP 401 with `status: null` and a generic Vietnamese message.
- Banned/deleted credentials return HTTP 403, numeric `status`, and the requested Vietnamese message.
- Active credentials return HTTP 200 with numeric `status`, one access JWT, and nullable `user`.
- Active login updates `Accounts.LastLoginAt`; no migration is required.

## JWT Configuration

Add this only to ignored `viora-BE/appsettings.json`, or provide `Jwt__Key` through the environment:

```json
"Jwt": {
  "Key": "replace-with-a-random-secret-of-at-least-32-bytes",
  "Issuer": "viora-BE",
  "Audience": "viora-client",
  "AccessTokenMinutes": 15
}
```

Do not commit the real key. Startup fails clearly when `Jwt:Key` is absent or too short.

## Security

- PBKDF2 verification occurs before status disclosure.
- Unknown account and wrong password share one response.
- Login is limited to 5 requests/minute per client IP.
- JWTs contain IDs, role, timestamps and token type; no email, phone, or password hash.
- Login returns access tokens only; refresh tokens are not issued.

## Verification

```powershell
dotnet build viora-BE.sln --no-restore -m:1 -c Release
dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --no-restore -m:1 -c Release
```
