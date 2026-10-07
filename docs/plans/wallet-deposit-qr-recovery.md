# Implementation Plan: Wallet deposit QR recovery

1. Add failing tests for blank provider QR values and frontend QR resolution.
2. Normalize QR/checkout values in the backend and frontend.
3. Add safe provider failure diagnostics without logging secrets or response bodies.
4. Run focused tests, full wallet tests, type-check, and builds.

No schema, dependency, authentication, or public contract changes are required.
