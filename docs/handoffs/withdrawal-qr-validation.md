# Withdrawal QR recipient validation

- VietQR Quick Link allows account identifiers up to 19 characters and positive transfer amounts up to 13 digits: https://www.vietqr.io/danh-sach-api/link-tao-ma-nhanh/.
- The backend's numeric bank-account policy previously accepted 6–25 digits. New accounts now accept 6–19 digits; unsupported legacy recipients are not approved/completed and receive no QR. Saved transfer amounts over 13 digits are also blocked.
- Failed/Rejected handling remains independent of recipient validity so invalid recipients can refund the full reserved amount, including fees, exactly once.
- Tests reproduce accepting an unsupported account and generating a QR for a modified unsupported recipient. After the fix, invalid-account save fails and an existing Processing withdrawal can fail, retain its reason/admin timeline and release held funds without duplicate refunds.
- Verification: 42 tests passed, one dedicated PostgreSQL concurrency test skipped; test run also builds the backend. No live DB rows or real bank transfers were changed.
- The corresponding admin fix adds required reason input directly inside the failure/rejection confirmation dialog. QR creation checks format, not real account existence or holder ownership.
