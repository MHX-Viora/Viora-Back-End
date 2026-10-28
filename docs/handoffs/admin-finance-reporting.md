# Admin finance reporting

## Delivered
- `/finance` defaults to overview. Navigation: overview, transactions, withdrawal requests, revenue, reconciliation, wallets and deposits. Sidebar label is Tài chính.
- Period flows (successful deposits, net bank payouts, ad revenue, withdrawal fees) are separate from current wallet balances and outstanding withdrawal queues.
- Revenue has inclusive Vietnam date filters, source filter, daily/monthly trend with accessible data table, paginated journal and UTF-8 BOM CSV. Source links open the original ledger entry or withdrawal detail.
- Transaction/deposit/withdrawal lists support dates/status; transactions also support type. Search, filters and pagination are URL state so links from reports work during client navigation.
- Live sent/received records are excluded by backend before transaction counting/pagination. No Live choice is rendered in Admin filters. Wallet reconciliation includes their internal balance snapshots.
- Retained existing manual withdrawal approval → processing → completed workflow, fee editor, bank recipient detail/QR, rejection/failure reason and ledger protections. Existing status mutation invalidates all finance queries.

## Recognition decision
Use a read-only journal over existing unique ledger/withdrawal IDs rather than a second writable revenue ledger. Completed advertisement captures contribute `-Amount`; explicitly advertisement-linked refunds contribute `-Amount` (negative). Completed withdrawals contribute stored `Fee` at `CompletedAt`. Deposits, principal, adjustments, holds, unused-budget releases and gifts do not contribute revenue. Current pricing/fee settings never recalculate historical income. No schema migration, backfill or new dependency.

## API
Admin role `2`, `Cache-Control: no-store`:
- `GET /api/admin/finance/overview?from=yyyy-MM-dd&to=yyyy-MM-dd`
- `GET /api/admin/finance/revenue?from=...&to=...&source=advertising|withdrawal_fee&page=1&pageSize=20`
- `GET /api/admin/finance/revenue/export` (same date/source filter, maximum 10,000 records; returns 422 with an actionable message if exceeded)
- `GET /api/admin/finance/reconciliation?page=1&pageSize=20`
- Existing transaction/payment/withdrawal endpoints accept `from`, `to`, `status`; transaction endpoint also accepts `type`. Existing callers remain compatible.

Dates default to current Vietnam month-to-date; maximum inclusive duration 366 days. UTC interval is `[from midnight -07h, day-after-to midnight -07h)`. Report and reconciliation totals use repeatable-read snapshots on PostgreSQL. Revenue aggregation and reconciliation are database queries; SQLite test fallback handles its unsupported decimal SUM in memory. All monetary production aggregation remains decimal.

Reconciliation checks latest available/held wallet snapshots, paid deposits without matching credits, completed withdrawals without matching capture/valid fee arithmetic, unresolved withdrawals without matching reservation, and campaign spent amounts against captured advertisement ledger amounts. It is read-only internal reconciliation, not a bank/payOS statement feed. Issues link to relevant data for investigation; no automatic repairs.

## Verification
- Backend finance/wallet/withdrawal/advertisement selection: 63 passed, 1 skipped (64 total). Includes 10 new reporting tests: actual service recognition/refunds/replay/historical fee, dates, source/pagination, filters, CSV, Live visibility, reconciliation, PostgreSQL SQL translation and real HTTP role/no-store/validation/export checks.
- Admin: 19 unit/component tests passed, ESLint and production build passed.
- Real Chromium with fixture HTTP API: queue → withdrawal detail and exact net amount; source/date/type/status filters; CSV matching filters; reconciliation → linked search; invalid range causes no request; empty/error/retry states; no document overflow at 1440/768/375px. Chrome DevTools additionally inspected actual rendered desktop/mobile screenshots.
- Browser fixture: `node src/pages/finance-reporting.browser.mjs` (cwd `viora-admin`). Optional `--serve` opens fixture at `http://127.0.0.1:4326/finance`; all data are fictitious.

## Limits
No live provider/bank transfer or production deployment was performed. Dedicated PostgreSQL integration environment is not configured; existing PostgreSQL audit test is skipped. SQL translation is checked without connecting, while SQLite integration validates behavior. Admin production bundle retains the existing >500 kB warning. SQLite fallback is intended for tests, not production data volume. Investigate reported historical mismatches before making manual adjustments.

## Files
Backend contracts/services: `Viora.Application/Wallets/AdminFinanceContracts.cs`, `Viora.Infrastructure/Wallets/AdminFinanceService.cs`, `AdminFinanceReconciliation.cs`; new reporting controller beside `AdminWalletController`; tests `Viora.Application.Tests/Wallets/AdminFinanceReportingTests.cs`.
Frontend: `src/components/FinanceReports.tsx`, `src/pages/FinancePage.tsx`, `src/services/admin-finance.service.ts`, `src/utils/finance-reporting.ts`, `src/types/finance.ts`, `src/finance.css` and reporting unit/browser tests.
