# Admin finance implementation plan

1. Reporting contract and tests (backend, medium): date/source validation, recognition and unique journal. Verify targeted finance tests, including expected red before implementation.
2. Reporting queries and API (backend, medium per file group): overview and paginated revenue with shared CSV source; admin authorization and no-store. Verify report totals, filters, date boundaries and export.
3. Reconciliation API (backend, medium): paginated actionable anomalies and totals; all internal ledger movements participate. Verify clean fixture and deliberately inconsistent records; no balance writes.
4. Admin reporting screens (frontend, medium per component): overview, revenue trend/table/export and reconciliation; extend transactions with date/type/status filters, hide Live server-side. Verify frontend behavior tests, lint and build.
5. Withdrawal integration (small): preserve detail/QR/approval and link overview queue, refresh reports after status changes. Verify existing lifecycle and detail tests.
6. Review and handoff: inspect diffs for recognition, authorization, query bounds and responsive states; browser verification and concise handoff with remaining runtime limitations.

Dependencies: 1 → 2 → 3 → 4 → 5 → 6. Checkpoint after backend: tests/build. Checkpoint after frontend: tests/lint/build/browser. Risks: historical data may be inconsistent (surface issues, no automatic repair); no external bank feed (label scope); large ad ledgers (database aggregation and bounded export).
