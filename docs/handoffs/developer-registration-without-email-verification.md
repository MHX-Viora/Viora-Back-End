# Developer registration without email verification

Registration now saves a contact email, accepts current terms and submits directly
for admin review. Host app and developer portal no longer request/confirm OTP.
Submission requires no DeveloperVerification OTP key or SMTP configuration.
Authentication, ownership, contact validation, terms, version checks and admin
approval remain enforced. Terms version: 2026-10-13.1.

`DeveloperApplication.EmailVerifiedAt` is nullable. Unverified submissions store
null; historical verification timestamps and records are preserved. Optional
legacy verification APIs remain available for older clients, but registration
does not call them. The verified flag is not fabricated.

Deploy BE, host app and admin portal together. Before accepting new submissions,
apply migration `20261013015728_MakeDeveloperEmailVerificationOptional`:

```powershell
dotnet ef database update --project .\Viora.Infrastructure --startup-project .\viora-BE --context AppDbContext
```

SQL: `docs/deployment/developer-email-verification-optional.sql`. It only drops
NOT NULL on EmailVerifiedAt. Downgrade is deliberately rejected while unverified
applications exist; reconcile records explicitly or restore a backup first.
No production deployment or database update was executed.

Validation: BE 192 passed, 6 environment-dependent skipped; admin build/lint and
48 tests passed; host TypeScript, targeted lint, 29 existing tests and 2 new
registration tests passed. EF reports no pending model changes. Browser inspected
actual portal components rendered with test data at terms/review steps, including
390px viewport; no OTP controls or console errors. Authenticated production
end-to-end submission was not tested.
