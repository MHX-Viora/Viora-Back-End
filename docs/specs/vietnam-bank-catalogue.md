# Vietnam bank catalogue

## Objective
Persist the Vietnam payment bank directory in the backend PostgreSQL database. Currently only an embedded transfer-supported JSON directory exists; `BankAccounts` stores users' accounts, not the bank directory.

## Scope and source
- Import the complete VietQR directory from https://api.vietqr.io/v2/banks, retrieved 2026-10-07 (65 entries).
- Preserve bank code, Vietnamese name, short name, BIN, logo, SWIFT code, transfer and lookup support.
- Clearly identify payment wallets/finance providers separately; this source is a payment directory, not an exhaustive licensed-bank register.
- Keep existing user bank accounts, wallet balances and withdrawal records intact.
- Target: the connection configured in `viora-BE/appsettings.json`, pending user confirmation.

## Implementation plan
1. Validate source rows: nonempty codes/names, unique codes and BINs; retain unsupported providers in the directory.
2. Prepare a PostgreSQL `Banks` table and transaction-wrapped, repeatable upsert seed keyed by bank code. Store provenance and source retrieval date. Existing withdrawal APIs remain compatible.
3. Execute against the confirmed target; verify imported row count and exact source fields, then rerun to prove no duplicates.

## Commands and structure
- Seed artifact: `tools/seed-vietnam-banks.sql`.
- Verification: source count/uniqueness checks and SQL readback within the target database.
- Backend regression check if application code changes: `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore --nologo --verbosity quiet -m:1`.
- Follow quoted PascalCase PostgreSQL identifiers used by existing EF tables; seed example: `INSERT INTO "Banks" (...) VALUES (...) ON CONFLICT ("Code") DO UPDATE ...`.

## Boundaries and acceptance
- Always validate external data, use transactions and avoid printing credentials.
- Confirm the DB target before remote writes; no database reset or unrelated migrations.
- Success: every source entry exists once in the DB with correct metadata; reruns are idempotent; existing financial data remains unchanged.
- Pending: DB target confirmation and review of this spec/plan before implementation, as required by the spec and planning skills.
