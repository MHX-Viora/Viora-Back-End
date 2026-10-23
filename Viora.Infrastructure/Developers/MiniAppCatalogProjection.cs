using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppCatalogProjection(AppDbContext db, MiniAppProductionOptions? options = null)
{
    public async Task AppendAsync(MiniAppPublication publication, MiniAppVersion version, DateTime now, CancellationToken ct)
    {
        if (options?.Enabled != true) return;
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Catalog projection requires the publication transaction.");
        var target = await MiniAppProductionEligibility.ReadAsync(db, publication.MiniAppId, now, ct);
        if (target.Publication.Id != publication.Id || target.Version.Id != version.Id || target.Version.SnapshotHash != version.SnapshotHash)
            throw new DeveloperAuthException(409, "PUBLICATION_CHANGED", "Phiên bản phát hành vừa thay đổi.");
        var previous = await db.Set<MiniAppCatalogPublication>().AsNoTracking().SingleOrDefaultAsync(x => x.PublicationId == publication.Id, ct);
        if (previous != null)
        {
            if (previous.MiniAppVersionId != version.Id || previous.SnapshotHash != version.SnapshotHash)
                throw new DeveloperAuthException(409, "PUBLICATION_CHANGED", "Phiên bản phát hành vừa thay đổi.");
            return;
        }
        var snapshot = target.Snapshot;
        var description = snapshot.ShortDescription ?? snapshot.Description;
        db.Add(new MiniAppCatalogPublication
        {
            PublicationId = publication.Id, MiniAppId = publication.MiniAppId, MiniAppVersionId = version.Id,
            ApprovedReviewRequestId = publication.ApprovedReviewRequestId, SnapshotHash = version.SnapshotHash,
            Name = snapshot.Name, Category = snapshot.Category, LogoUrl = snapshot.LogoUrl,
            Description = description == null ? null : string.Concat(description.EnumerateRunes().Take(300).Select(x => x.ToString())),
            VersionName = version.VersionName, WebsiteUrl = snapshot.WebsiteUrl!, Domain = snapshot.Domain!,
            SsoEnabled = snapshot.SsoEnabled, PublishedAt = publication.PublishedAt,
            ProductionClientKey = snapshot.SsoEnabled ? snapshot.Production!.ClientId : null,
            ProductionClientType = snapshot.SsoEnabled ? snapshot.Production!.ClientType : null,
            ProductionScopes = snapshot.SsoEnabled ? string.Join(' ', snapshot.Production!.Scopes) : null
        });
        await db.SaveChangesAsync(ct);
    }
}
