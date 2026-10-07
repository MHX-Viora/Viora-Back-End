# ANKT wallet history handoff

## Flow and data

- `POST /api/wallet/deposits` creates one `Payment.Pending` and one linked `WalletTransaction.Deposit/Pending`. Creating a QR leaves available balance unchanged.
- A verified payOS success completes that ledger and credits available balance inside the same database transaction. The payment row is locked before checking status, so repeated success callbacks do not credit twice. Existing `Payment` and ledger uniqueness constraints protect idempotency keys and links.
- Cancellation, failure, and expiry update the payment status and pending ledger without credit. A later authenticated paid confirmation for an expired payment can still settle it, as required by the existing payment lifecycle.
- `GET /api/wallet/transactions` merges deposit payments with other wallet ledger entries before sorting and paging. The payment ID is the stable public history ID for a deposit. A linked or reference-matched deposit ledger is suppressed as a separate list entry. Old payment rows without a ledger still appear, with no balance snapshot.
- The optional `group` query selects deposit, withdrawal, payment, or refund transactions by type. Omitting it returns all wallet activity, including holds and advertisement spend. The `type` query remains available.
- Source and destination are derived from transaction type and existing withdrawal/ad details. No database migration is needed.

## Advertising money

- Submission moves the total budget from available to held balance. Each charged event captures from held and increments `SpentAmount` while decrementing `ReservedAmount` in the same transaction.
- Cancellation, rejection, or completion releases only `ReservedAmount`. Ledger idempotency keys prevent repeating the same hold, charge, or release.

## Verification

- `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false`
- `npm test` and `npx tsc --noEmit` in the app directory.
- The SQLite integration tests cover pending, paid, repeated success, failure, cancellation, expiry, legacy payment links, distinct deposits, idempotency key mismatch, and partial advertising spend/refund.
- A live payOS and concurrent PostgreSQL webhook check remains necessary before production rollout; these integrations are not simulated by SQLite.
