# Database Entities Handoff

## Delivered

- 24 public domain entities covering accounts, identity, content, social graph, messaging, notifications, and reports.
- EF Core 8/Npgsql mappings with PostgreSQL types, keys, relationships, defaults, checks, indexes, and UTC timestamp stamping.
- Initial migration under `Viora.Infrastructure/Persistence/Migrations`.
- Infrastructure registration through `services.AddInfrastructure(configuration)`.
- xUnit metadata tests for table coverage, UTC types, delete behavior, partial identity uniqueness, configuration validation, and unordered friendship uniqueness.

## Runtime Configuration

Copy `viora-BE/appsettings.example.json` to `viora-BE/appsettings.json`, then set the local PostgreSQL connection string. The real file is ignored by Git.

```powershell
Copy-Item .\viora-BE\appsettings.example.json .\viora-BE\appsettings.json
dotnet run --project .\viora-BE\viora-BE.csproj
```

The application intentionally fails at startup when the value is empty. The design-time factory finds the API project's `appsettings.json` from the solution root, API project, or Infrastructure project and validates Npgsql `Host=...;Database=...` syntax.

## Migration Commands

```powershell
dotnet ef migrations script --project .\Viora.Infrastructure --startup-project .\viora-BE
dotnet ef database update --project .\Viora.Infrastructure --startup-project .\viora-BE
```

Do not run `database update` until a reviewed target connection string is configured. Soft-delete columns are mapped but no global query filters are enabled.
