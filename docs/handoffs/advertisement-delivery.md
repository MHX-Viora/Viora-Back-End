# Handoff: Advertisement delivery and native placements

## Root causes

- `POST /api/advertisements` accepts campaigns from Personal accounts, but `GET /api/advertisements/delivery` previously excluded their posts by requiring Creator or higher. The same campaign could be Active with held budget yet never enter delivery.
- The client first inserted Feed and News ads after too many organic items for short lists, and requested delivery only on the first Feed page. Eligible inventory could therefore remain unseen.
- News hid the post header containing the sponsorship disclosure. Reels counted an impression from scroll index alone, before a verified viewing interval.

## Changes

- Delivery now admits every supported advertiser account style, while retaining Active status, public undeleted content, active account, UTC window, held balance, placement, targeting, feedback, and daily cap checks. Production PostgreSQL keeps SQL-side decimal budget filtering and row locking; SQLite integration tests use decimal evaluation in memory.
- Feed, Reels, and News insert sponsored items earlier, including lists with two organic items. Feed requests delivery on later pages and excludes ads already inserted in that list.
- Feed and News show a visible sponsored label, actual creative, destination preview, and campaign CTA. Sponsored News cards route through ad click tracking. Reels use a sponsored full-screen card and record an impression only after at least 60% visibility for 1 second while focused and active.
- Metrics refresh when the advertiser returns to the campaign detail screen. Impression requests retain a stable event ID on retries; the server deduplicates repeat viewer events and captures spend from the wallet.

## API and accounting

- Delivery: `GET /api/advertisements/delivery?placement={0|1|2}&take=3` for Feed, Reels, News.
- Tracking: impression and click endpoints in `AdvertisementsController`; `AdvertisementService.RecordEventAsync` handles event idempotency, daily budget, charge capture, and metrics. Default charges are 100 VND per impression and 500 VND per click. CTR is clicks divided by impressions times 100.
- The 50,000 VND campaign hold is delivery balance (`ReservedAmount`), not spend. Creation, pause, resume, cancellation, and expiry continue through the existing lifecycle. No schema migration is required.

## Verification and limits

- Live Neon dev check (2026-09-23 08:27 UTC): campaign `74020dda-84f4-4325-a70c-516e8aa1a1bf` is Active for Feed, within its UTC window, with 50,000 VND held and zero spend. Its advertiser is the ANKT account shown in the screenshot. `GetDeliveryAsync` returns zero items to that advertiser because self-delivery is excluded, and returns the campaign to another user. Self impression/click tracking is also forbidden, so the advertiser's own viewing cannot raise dashboard metrics. The only other campaign is Cancelled; no active Reels or News campaign exists in this database.
- Community Feed now reads the advertiser's Active campaigns through the existing `mine` API for a clearly labeled owner preview after two organic posts. The preview does not send impression or click events. On screen focus the Feed reloads, so an approval or campaign change is reflected after returning from management. Charged delivery to other viewers still uses the existing delivery API.
- SQLite integration coverage exercises Personal-account delivery in all three placements, status and schedule transitions, targeting/visibility/budget filters, impression and click deduplication, wallet spend, CTR, and refunds.
- Frontend insertion and navigation tests plus TypeScript checks cover the changed contracts. See the completion report for final test counts.
- No authenticated production campaign, database connection, or browser session was available for checking a specific campaign record. Expo Metro failed to start a browser build in this sandbox with `spawn EPERM`, so visual device inspection remains to be done in a working app environment.
