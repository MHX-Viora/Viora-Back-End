# Spec: Social Database Model

## Objective

Provide the .NET 8 backend with a code-first PostgreSQL model for the 24 tables described in `social app.docx`. The model must expose public domain entities, explicit relationships, safe constraints, and an initial migration without applying it to a database.

## Tech Stack and Commands

- .NET 8, EF Core 8.0.11, Npgsql EF Core 8.0.11, xUnit.
- Restore: `dotnet restore viora-BE.sln`
- Build: `dotnet build viora-BE.sln --no-restore`
- Test: `dotnet test viora-BE.sln --no-build`
- Migration script: `dotnet ef migrations script --project Viora.Infrastructure --startup-project viora-BE`

## Project Structure

- `Viora.Domain/Entities`: public entities, base types, and enums.
- `Viora.Infrastructure/Persistence`: DbContext, mappings, DI, and migrations.
- `Viora.Infrastructure.Tests`: EF metadata and configuration tests.
- `viora-BE`: API composition root and non-secret configuration key.

## Code Style

```csharp
public sealed class Account : AuditableEntity
{
    public required string Email { get; set; }
    public AccountRole Role { get; set; } = AccountRole.User;
}
```

Use nullable reference types, singular C# entity names, plural database table names, UTC `DateTime`, explicit Fluent API mappings, and enums backed by `short`.

## Testing Strategy

Fast model-metadata tests verify entity count, keys, relationships, column types, indexes, filters, and delete behavior. Migration generation verifies PostgreSQL SQL can be produced without connecting to a database.

## Boundaries

- Always: preserve documented columns; use UTC timestamps; keep credentials outside source control.
- Ask first: applying migrations or changing the approved schema semantics.
- Never: store plaintext passwords/identity images or commit a real connection string.

## Success Criteria

- EF discovers exactly 24 domain entities and maps all documented tables.
- Required unique/check/partial indexes and restrictive delete behavior are present.
- The solution builds, tests pass, and an idempotent migration script is generated.
- Startup reports a clear error when `ConnectionStrings:DefaultConnection` is absent.

## Approved Decisions

PostgreSQL; full EF setup; strongly typed enums; `ReportReason.Other = 0`; UTC `timestamptz`; identity submission history with one pending submission per user; restrictive deletes with cascade only for pure dependents/join rows; no soft-delete query filters.
