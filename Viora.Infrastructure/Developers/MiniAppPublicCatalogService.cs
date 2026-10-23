using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppPublicCatalogService(AppDbContext db, TimeProvider clock, MiniAppProductionOptions options, bool ssoEnabled = false)
{
    public async Task<MiniAppPublicPageDto> ListAsync(MiniAppPublicCatalogQuery request, CancellationToken ct)
    {
        RequireEnabled(); request.Validate();
        var query = Query(clock.GetUtcNow().UtcDateTime);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Projection.PublishedAt).ThenBy(x => x.Projection.PublicationId)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new MiniAppPublicItemDto(x.AppId, x.Projection.Name, x.Projection.LogoUrl,
                x.Projection.Description, x.Projection.Category, x.Projection.VersionName)).ToArrayAsync(ct);
        return new(items, request.Page, request.PageSize, total);
    }

    public async Task<MiniAppPublicDetailDto> DetailAsync(string appId, CancellationToken ct)
    {
        RequireEnabled();
        var row = await Query(clock.GetUtcNow().UtcDateTime).SingleOrDefaultAsync(x => x.AppId == appId, ct);
        if (row == null) throw Unavailable();
        var target = await MiniAppProductionEligibility.ReadAsync(db, row.Projection.MiniAppId, clock.GetUtcNow().UtcDateTime, ct);
        if (target.Publication.Id != row.Projection.PublicationId || target.Version.SnapshotHash != row.Projection.SnapshotHash)
            throw Unavailable();
        var item = row.Projection;
        return new(new(row.AppId, item.Name, item.LogoUrl, item.Description, item.Category, item.VersionName), true,
            item.SsoEnabled, ["platform.getContext", "platform.close"]);
    }

    private IQueryable<CatalogRow> Query(DateTime now) =>
        from item in db.Set<MiniAppCatalogPublication>().AsNoTracking()
        join publication in MiniAppProductionEligibility.Publications(db, now) on item.PublicationId equals publication.Id
        join version in db.MiniAppVersions.AsNoTracking() on item.MiniAppVersionId equals version.Id
        join app in db.MiniApps.AsNoTracking() on item.MiniAppId equals app.Id
        where item.SnapshotHash == version.SnapshotHash
            && db.MiniAppDomainVerifications.Any(proof => proof.MiniAppId == item.MiniAppId && proof.Hostname == item.Domain
                && proof.Status == MiniAppDomainStatus.Verified && proof.RevokedAt == null && proof.ExpiresAt > now)
            && (!item.SsoEnabled && publication.ProductionOAuthClientId == null || item.SsoEnabled && ssoEnabled &&
                db.MiniAppOAuthClients.Any(client => client.Id == publication.ProductionOAuthClientId && client.MiniAppId == item.MiniAppId
                    && client.Environment == MiniAppOAuthEnvironment.Production && client.RevokedAt == null
                    && client.ClientKey == item.ProductionClientKey && client.ClientType == item.ProductionClientType
                    && client.AllowedScopes == item.ProductionScopes && (item.ProductionClientType == "Public" ||
                        db.MiniAppClientSecrets.Any(secret => secret.OAuthClientId == client.Id && secret.RevokedAt == null &&
                            (secret.ExpiresAt == null || secret.ExpiresAt > now)))))
        select new CatalogRow { AppId = app.AppId, Projection = item };

    private void RequireEnabled() { if (!options.Enabled) throw Unavailable(); }
    private static DeveloperAuthException Unavailable() => new(404, "APP_NOT_FOUND", "Mini App hiện không khả dụng.");
    private sealed class CatalogRow
    {
        public string AppId { get; init; } = "";
        public MiniAppCatalogPublication Projection { get; init; } = null!;
    }
}
