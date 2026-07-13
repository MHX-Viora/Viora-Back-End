# Spec: Account CRUD

## Objective

Expose account registration and CRUD without leaking password hashes. Registration accepts one identifier, detects email or phone, normalizes it, and hashes the password.

## Contract

- `POST /api/accounts/register` accepts `{ identifier, password }`, creates an active User account, and returns `201` with `{ "message": "Register success" }`.
- `GET /api/accounts?page=1&pageSize=20` returns a paginated response; page size is capped at 100.
- `GET /api/accounts/{id}` returns `200` or `404`.
- `PUT /api/accounts/{id}` replaces editable fields and returns `200` or `404`.
- `DELETE /api/accounts/{id}` is idempotent and returns `204`.
- Duplicate email/phone returns `409`; an invalid identifier returns `400` RFC problem details.

## Structure and Style

- Application owns DTOs, service contracts, validation-independent business flow, and persistence abstractions.
- Infrastructure implements EF persistence and PBKDF2 password hashing.
- Web API owns request validation and HTTP status mapping.
- Async methods accept cancellation tokens; entities never cross the API boundary.

## Testing

Run `dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj`. Cover password hashing, duplicate prevention, normalization, pagination, update-not-found, and idempotent soft-delete.

## Boundaries

- Always: parameterized EF queries, normalized email, hashed passwords, no `PasswordHash` response.
- Ask first: hard-delete or schema changes.
- Never: log passwords/hashes or return the entity directly from controllers.

## Success Criteria

Solution builds, all tests pass, and the migration permits email-only or phone-only accounts while requiring at least one identifier.
