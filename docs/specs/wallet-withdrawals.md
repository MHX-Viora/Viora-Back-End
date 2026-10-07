# Spec: Ví ANKT withdrawals and coin balance

## Objective

Extend the existing ANKT wallet instead of creating a parallel wallet system. Users can manage payout bank accounts, create an idempotent withdrawal request, confirm it before submission, inspect its lifecycle, and see it in the existing wallet ledger. The utilities wallet card also exposes a separate persisted ANKT coin balance.

## Approved scope and assumptions

- Reuse `Wallet`, `WalletTransaction`, `Payment`, `IWalletService`, and existing `/api/wallet` routes.
- No bank payout provider exists in the repository. This increment records a production withdrawal request and reserves funds; an authorized admin records manual processing outcomes. It does not claim to transfer money externally.
- A withdrawal is funded only from VND `AvailableBalance`; ANKT coins cannot be withdrawn or converted.
- ANKT coins are a separate integer balance. Coin earning/spending/history are intentionally not introduced until their business rules exist.
- Bank account numbers are encrypted at rest. API responses expose only the last four digits.
- Withdrawal fee, minimum, and maximum are server configuration. The client cannot supply or override them.

## API contract

- `GET /api/wallet`: existing response plus additive `anktCoinBalance`.
- `GET /api/wallet/bank-accounts`: list the authenticated user's masked accounts.
- `POST /api/wallet/bank-accounts`: add an owned payout account.
- `POST /api/wallet/withdrawals`: create or replay an idempotent request.
- `GET /api/wallet/withdrawals/{id}`: return an owned withdrawal.
- `POST /api/wallet/withdrawals/{id}/cancel`: cancel an owned pending withdrawal and release funds.
- `GET /api/admin/finance/withdrawals`: admin-only paginated review queue.
- `PATCH /api/admin/finance/withdrawals/{id}/status`: admin-only valid lifecycle transition.

Error responses retain the existing `{ error: { code, message, details? } }` shape.

## Withdrawal lifecycle

- `Pending -> Processing -> Completed`
- `Pending -> Rejected | Cancelled`
- `Processing -> Completed | Failed | Rejected`
- Terminal statuses cannot transition again.
- Creating a withdrawal moves VND from available to held under a serializable transaction and row lock.
- Completed captures held funds; failed/rejected/cancelled releases held funds.
- Every balance mutation has a linked `WalletTransaction` record.

## Security boundaries

- Always derive user and wallet ownership from the authenticated claim and database.
- Validate amount, bank-account ownership, wallet status, configured limits, and idempotency on the server.
- Never accept balance, fee, net amount, user ID, or wallet ID from the client.
- Never return, log, or persist plaintext account numbers.
- Require `Wallet__BankAccountEncryptionKey` before bank-account operations.
- Prevent concurrent overdrafts with PostgreSQL row locks and serializable transactions.

## Commands

- Backend build: `dotnet build .\viora-BE.sln --no-restore -m:1`
- Backend test: `dotnet test .\Viora.Application.Tests\Viora.Application.Tests.csproj --no-restore -m:1`
- Migration: `dotnet ef migrations add AddWalletWithdrawals --project .\Viora.Infrastructure --startup-project .\viora-BE`
- Frontend test: `npm test`
- Frontend lint: `npm run lint`
- Frontend type-check: `npx tsc --noEmit`

## Project structure

- `Viora.Domain/Entities`: wallet aggregate entities and enums.
- `Viora.Application/Wallets`: contracts, rules, and service interfaces.
- `Viora.Infrastructure/Wallets`: wallet, withdrawal, and bank-account implementations.
- `viora-BE/Controllers`: authenticated user/admin REST boundaries.
- `viora/features/wallet`: withdrawal, history, and transaction UI.
- `viora/components/wallet`: reusable wallet and ANKT coin components.

## Testing strategy

- Unit-test withdrawal rules, lifecycle transitions, masking, and cryptographic round trips.
- Contract-test authenticated routes, ownership filters, idempotency, and server-derived fee semantics through source contracts where the existing suite uses that pattern.
- Run all backend and frontend tests, lint, type-check, build, and migration validation.

## Boundaries

- Always: preserve deposit behavior, existing navigation style, theme tokens, ledger traceability, ownership checks, masked bank data.
- Never: create duplicate wallet tables, withdraw coins, fake payout success, use production mock data, log account numbers, trust client financial values.
- Out of scope: external payout provider integration, coin earning/spending/conversion, bank-account editing/deletion, automatic payout reconciliation.

## Success criteria

- Three compact card actions work at 320px and above.
- Withdrawal confirmation precedes the network request and blocks duplicate submit.
- Concurrent withdrawal requests cannot overdraw the wallet.
- User history and detail screens show Vietnamese status text and masked payout data.
- Light/dark modes use the existing theme system with no component-level color literals.
- Existing deposit tests and flow remain passing.
