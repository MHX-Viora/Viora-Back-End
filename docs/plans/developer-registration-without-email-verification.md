# Developer registration without email verification

User explicitly requests removal of Developer email verification, superseding
the earlier proposal to reuse verified ANKT email.

Scope: remove OTP controls/gates in host and developer portal; submit a valid
contact email and current terms directly for admin review. Never mark an
unverified email verified. Preserve historical OTP records and optional endpoints
for compatibility; they are not called by registration.

Plan: reproduce submission without mail configuration in service/HTTP tests;
make application EmailVerifiedAt nullable via a new migration; remove submission
verification gate; expose emailVerificationRequired=false; simplify both UIs
and published instructions; verify backend tests, builds, frontend tests and lint.

Acceptance: missing OTP key/SMTP cannot block registration; invalid contact
email, rejected terms, non-owner/inactive accounts and stale versions still fail;
submitted draft is immutable and admin approval remains required. Existing
EmailVerifiedAt timestamps survive migration; new unverified applications store
null. Production deployment/migration is not part of this change.
