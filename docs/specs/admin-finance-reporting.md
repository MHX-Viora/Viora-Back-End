# Admin finance reporting

## Objective and accepted scope
Extend the existing Admin finance area with overview, transactions, withdrawal requests, revenue and reconciliation. Retain wallets/deposits and the existing manual bank-transfer workflow. Hide Live gift transactions in Admin lists, filters and revenue reports; retain their internal ledger effects for accurate wallet reconciliation.

## Rules
- Revenue is completed withdrawal `Fee` at `CompletedAt` plus completed advertisement captures, less explicitly advertisement-linked refunds. Use stored amounts, never today's fee/pricing settings.
- Deposits, transfers, holds, releases, adjustments and withdrawal principal are not revenue. Release of unused ad budget is not a revenue refund.
- Derive a read-only revenue journal from unique existing ledger/withdrawal IDs; do not introduce a second mutable ledger or migrate historical balances.
- Dates are inclusive Vietnam calendar dates, converted to UTC half-open intervals. Maximum report interval: 366 days. Default: current month.
- Current wallet balances and outstanding withdrawal counts are current snapshots, labeled separately from period flows.
- Revenue detail, daily/monthly totals and CSV use the same source query and filters. Paginate detail. CSV is bounded and fails explicitly rather than truncating silently.
- Reconciliation detects wallet snapshot/held mismatches, paid deposits without matching credits, completed withdrawals without matching settlement, and advertisement spend without matching captures. It is internal ledger reconciliation, not bank-statement verification. Never auto-adjust money.
- Retain admin-only authorization, parameterized EF queries, structured errors, no-store responses and CSV formula-injection protection.

## Structure and style
Backend: `Viora.Application/Wallets` contracts, `Viora.Infrastructure/Wallets` queries, existing Admin controller. Frontend: existing React Query service, typed DTOs, Finance page/report components, existing CSS tokens. Example: `Task<AdminPage<RevenueItem>> GetRevenueAsync(...)`; monetary values remain decimal VNĐ on the server.

## Commands and testing
- Backend: `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false` (cwd `viora-BE`).
- Admin: `node --test src/pages/finance-reporting.test.mjs src/pages/withdrawal-detail.test.mjs src/components/withdrawal-fee-settings.test.mjs`; `npm run lint`; `npm run build` (cwd `viora-admin`).
- Tests: report recognition, non-revenue exclusion, stable historical fee, Vietnam day boundaries, invalid dates/ranges, source filters, pagination, reconciliation mismatches and CSV escaping; retain withdrawal lifecycle regression tests. Verify browser desktop/mobile with fixture API responses when a test backend is unavailable.

## Boundaries and success
Always preserve existing wallet writes and withdrawal transitions; no new dependencies, credentials, deployment or commits required. No production payment actions during verification. User approved implementation of this scope in conversation. Each report must link to an existing source, never count pending money as revenue, and display loading/error/empty states.
