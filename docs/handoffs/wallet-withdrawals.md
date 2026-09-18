# ANKT wallet withdrawals handoff

## Delivered

- VND and ANKT coin balances are separate; coins cannot be withdrawn or converted.
- User-owned bank accounts store AES-GCM ciphertext, a keyed hash, and last four digits only for display.
- Withdrawal creation calculates fee/net server-side, locks the wallet row, moves money from available to held, and writes a pending ledger entry.
- Admin lifecycle supports Pending → Processing → Completed/Failed/Rejected and user cancellation while Pending.
- Completion captures held funds; failure, rejection, or cancellation releases them. Every balance mutation has a ledger entry.
- Duplicate submissions are idempotent; reusing a key with a different payload is rejected.
- Frontend includes deposit/withdraw/history actions, themed wallet/coin balances, bank-account creation, server quote, confirmation sheet, history filters, and withdrawal metadata/status details.

## Operations

Configure `Wallet__BankAccountEncryptionKey` with a stable base64-encoded 32-byte value. Also configure fee/min/max values from `.env.example`.

Apply migration:

```powershell
dotnet ef database update --project Viora.Infrastructure/Viora.Infrastructure.csproj --startup-project viora-BE/viora-BE.csproj
```

No external bank payout provider exists in this repository. Admin endpoints record the real manual processing lifecycle; they do not claim that a transfer was sent automatically.
