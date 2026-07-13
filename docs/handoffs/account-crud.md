# Account CRUD Handoff

## API

- `POST /api/accounts/register` detects email/phone from `identifier`, hashes the password, and returns `{ "message": "Register success" }`.
- `GET /api/accounts?page=1&pageSize=20` lists non-deleted accounts with pagination.
- `GET /api/accounts/{id}` returns one non-deleted account.
- `PUT /api/accounts/{id}` updates email, phone, and optionally password.
- `DELETE /api/accounts/{id}` performs an idempotent soft-delete.

Requests are validated by ASP.NET DataAnnotations plus application validation. Invalid identifiers return 400; duplicate email/phone returns 409. Responses never include `PasswordHash`.

## Security

Passwords use PBKDF2-HMAC-SHA256 with a random 16-byte salt, 600,000 iterations, a 32-byte hash, and constant-time verification. Account endpoints are not yet connected to an authentication scheme; add admin authorization before exposing list/read/update/delete outside development.

## Verification

```powershell
dotnet build .\viora-BE.sln --no-restore
dotnet test .\Viora.Infrastructure.Tests\Viora.Infrastructure.Tests.csproj --no-build --no-restore
```
