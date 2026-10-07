# Bank account save failure

- Local `.env` contained a placeholder for `Wallet__BankAccountEncryptionKey`; AES-GCM/HMAC requires a Base64-encoded 32-byte key. Saving a recipient threw `InvalidOperationException` and returned HTTP 500.
- Replaced the invalid local value with a cryptographically random, persistent key. The secret stays in ignored `.env`; restart the backend to reload it. Hosted environments must configure their own stable valid key.
- Invalid encryption configuration now raises `WalletConfigurationException`; the API logs configuration details server-side and returns HTTP 503 with `BANK_ACCOUNT_SERVICE_UNAVAILABLE` and a safe Vietnamese message.
- Preserve the configured key across restarts and deployments; changing a working key makes previously encrypted bank accounts unreadable.
- Regression coverage: malformed/empty/wrong-length keys and saving a Vietcombank recipient with a Vietnamese holder name, encrypted account number, masked response and a single default account.
- Verify: `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore --nologo --verbosity quiet -m:1 --disable-build-servers`.
