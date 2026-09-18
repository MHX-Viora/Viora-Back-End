# Spec: Wallet deposit QR recovery

## Objective

Ensure an authenticated wallet deposit either returns usable QR content or a clear
provider error. A blank `qrCode` must fall back to a non-blank payOS checkout URL.

## Scope and boundaries

- Preserve the existing payOS request, signature verification, wallet ledger, and API contract.
- Normalize whitespace-only provider values at the backend and frontend boundaries.
- Log only provider status/error codes and the internal order code; never log credentials or response bodies.
- Do not add dependencies, change the database schema, or create a real payment during automated tests.

## Commands

- Backend: `dotnet test .\Viora.Application.Tests\Viora.Application.Tests.csproj --no-restore -m:1 /nodeReuse:false`
- Backend build: `dotnet build .\viora-BE.sln --no-restore -m:1 /nodeReuse:false`
- Frontend: `npm test`
- Frontend types: `npx tsc --noEmit`

## Testing strategy

- Unit-test blank/whitespace QR fallback and missing checkout data.
- Contract-test that the deposit screen uses the shared resolver.
- Build both projects after focused tests pass.

## Success criteria

- Blank `qrCode` with a valid `checkoutUrl` renders a QR.
- Blank values are never passed to `react-native-qrcode-svg`.
- Missing provider payment data returns a clear API error.
- Existing wallet tests and builds pass.
