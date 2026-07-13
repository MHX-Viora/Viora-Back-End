# Spec: Account Login

## Objective

Add `POST /api/accounts/login` for email or phone authentication. Normalize the identifier exactly like registration, verify the password without exposing whether an account exists, return Vietnamese status messages, and issue a signed access JWT only for active accounts.

## API Contract

- Request: `{ "identifier": string, "password": string }`.
- Unknown account or wrong password: HTTP 401, `status: null`, message `Thông tin đăng nhập hoặc mật khẩu không chính xác.`
- Banned (`status = 0`): HTTP 403 with the requested community-standards message.
- Active (`status = 1`): HTTP 200 with `status`, `accessToken`, and nullable `user`.
- Deleted (`status = 2`): HTTP 403 with `Tài khoản này không còn tồn tại hoặc đã bị xóa.`
- Password is verified before account status is disclosed.

## Security and Configuration

- JWTs use HMAC-SHA256. Access lifetime is 15 minutes.
- Signing key comes from `Jwt:Key` / `Jwt__Key`, is never committed, and must be at least 32 UTF-8 bytes.
- Authentication endpoints use ASP.NET Core rate limiting.
- Password hashes and identifiers are never included in login responses or tokens.

## Structure and Testing

- Application owns login contracts and business decisions.
- Infrastructure owns EF lookup and token creation.
- API owns HTTP mapping and request validation.
- xUnit covers email/phone lookup, bad credentials, all statuses, nullable user, token issuance, and validation metadata.

## Commands

```powershell
dotnet build viora-BE.sln --no-restore -m:1 -c Release
dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --no-build --no-restore -c Release
```

## Boundaries

- Always: generic invalid-credential response; constant-time password verification; status included in every login result.
- Ask first: adding refresh tokens or database schema changes.
- Never: hard-code JWT secrets, return password hashes, or issue tokens to banned/deleted accounts.

## Success Criteria

All status branches match the contract, active login updates `LastLoginAt`, tests pass, and the solution builds without warnings.
