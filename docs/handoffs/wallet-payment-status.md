# Wallet payment status handoff

## Deposit lifecycle

- QR creation: `Payment.Pending` and `WalletTransaction.Pending`; wallet balance is unchanged. Leaving the QR screen or closing the app keeps it pending so the user can resume it; this is not a cancellation.
- Explicit cancellation: call payOS `POST /v2/payment-requests/{orderCode}/cancel`, verify the signed response and order code, then set `Payment.Cancelled` and the linked ledger to `Failed`. A failed provider call leaves the payment pending.
- Provider confirmation: a verified payOS webhook or signed status lookup calls `CompleteDepositAsync`. It credits available balance and marks both payment and ledger complete in one database transaction. Duplicate confirmations are idempotent.
- Expiry: a pending payment past its 15-minute deadline becomes `Expired`; its uncredited ledger becomes `Failed`. A later authenticated provider `PAID` result can still credit an expired payment.
- Wallet transaction list checks recent pending/expired payOS payments before returning data. The deposit detail endpoint also reconciles its payment. The app refreshes history on focus and labels an open QR as “Chờ thanh toán”.

## Other wallet flows

- Withdrawal: available balance moves to held on request. Completion consumes held funds; cancellation, rejection, or failure releases them. The withdrawal status controls the user-facing label.
- Wallet holds, captures, releases, refunds, transfers, and adjustments have completed ledger rows when their balance mutation commits. Pending deposit rows are never treated as credited funds in the app.

## Operational check

After deployment, verify one cancelled QR, one expired QR, and one real paid deposit using payOS sandbox or a controlled account. Confirm the payment status, ledger status, wallet balance, and history label agree. No live payment was initiated by automated tests.
