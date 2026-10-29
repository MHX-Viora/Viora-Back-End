# Admin gift SVGA storage

- One existing LiveGifts table gains AnimationData (bytea, nullable) and
  AnimationDurationMs (int, default 0). Migration:
  `20261029080031_AddGiftSvgaData`; inspect/apply through normal deployment.
  No separate animation table or new gift prices/seeds.
- New admin role-2 multipart endpoints POST `api/live-gifts/admin/upload`,
  PUT `api/live-gifts/admin/{id}/upload` accept metadata JSON, optional animation
  IFormFile and removeAnimation bool. Validate metadata and SVGA fully before a
  single SaveChanges; omitted file preserves the existing animation. Replacement
  and removal are supported. Existing JSON APIs and procedural gifts remain.
- SvgaMetadata validates SVGA-2 zlib/protobuf params: <=5MB file/24MB inflated,
  positive <=4096 dimensions, fps 1–60, <=1800 frames and <=30 seconds. Duration
  is measured from source; SVGA cannot be combined with a procedural effect.
- GET `api/live-gifts/{id}/animation.svga` serves public bytes with nosniff and
  no-store. Catalog/admin DTO projections include URL/duration, not bytes.
  LiveGift realtime notification exposes URL/duration for host/viewer players.
  Payment/ledger calculations and replay handling are unchanged.
- Self-hosted Apache-2.0 SVGAPlayer-Web 2.3.1 bundle/license under
  wwwroot/gift-effects. Admin carries user-supplied preset files and previews.
- LiveGiftUploadTests passed (validation, exact bytes persistence, update,
  replacement/removal, public bytes, active catalog, role-2 attributes, no-zero
  prices). Tests use an isolated in-memory SQLite DB, not a live database.
- Admin/client integration and checks documented in sibling repositories'
  docs/handoffs. No migration application, deployment or commit was performed.

Check: `dotnet test Viora.Application.Tests --no-restore --filter FullyQualifiedName~LiveGiftUploadTests --verbosity minimal -m:1 -nodeReuse:false`.
