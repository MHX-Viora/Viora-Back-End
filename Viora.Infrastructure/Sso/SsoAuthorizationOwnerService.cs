using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Viora.Application.Sso;
using Viora.Domain.Developers;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Sso;

public sealed class SsoAuthorizationOwnerService(AppDbContext db, IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens, TimeProvider clock)
{
    public async Task<IReadOnlyList<SsoAuthorizationDto>> ListAsync(Guid owner, CancellationToken ct, int offset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var rows = await (from consent in db.MiniAppUserConsents.AsNoTracking()
            join app in db.MiniApps.AsNoTracking() on consent.MiniAppId equals app.Id
            where consent.AccountId == owner
            orderby consent.GrantedAt descending, consent.Id
            select new { Consent = consent, app.AppId, app.Name }).Skip(offset).Take(100).ToListAsync(ct);
        return rows.Select(x => Map(x.Consent, x.AppId, x.Name)).ToArray();
    }
    public async Task<SsoAuthorizationDto?> GetAsync(Guid owner, Guid id, CancellationToken ct)
    {
        var row = await (from consent in db.MiniAppUserConsents.AsNoTracking()
            join app in db.MiniApps.AsNoTracking() on consent.MiniAppId equals app.Id
            where consent.AccountId == owner && consent.Id == id
            select new { Consent = consent, app.AppId, app.Name }).SingleOrDefaultAsync(ct);
        return row == null ? null : Map(row.Consent, row.AppId, row.Name);
    }
    public async Task<bool> RevokeAsync(Guid owner, Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var consent = await db.MiniAppUserConsents.SingleOrDefaultAsync(x => x.Id == id && x.AccountId == owner, ct);
        if (consent == null) return false;
        // Also clean up tokens belonging to already revoked historical grants.
        if (consent.AuthorizationId != null)
        {
            var authorization = await authorizations.FindByIdAsync(consent.AuthorizationId, ct);
            if (authorization != null && await authorizations.GetStatusAsync(authorization, ct) != OpenIddictConstants.Statuses.Revoked &&
                !await authorizations.TryRevokeAsync(authorization, ct))
                throw new DbUpdateConcurrencyException("Authorization changed during revocation.");
            await tokens.RevokeByAuthorizationIdAsync(consent.AuthorizationId, ct);
        }
        if (consent.RevokedAt == null)
        {
            consent.RevokedAt = clock.GetUtcNow().UtcDateTime; consent.UpdatedAt = consent.RevokedAt.Value; consent.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct); return true;
    }
    private static SsoAuthorizationDto Map(MiniAppUserConsent consent, string appId, string name) => new(consent.Id, appId, name,
        consent.Environment.ToString(), consent.GrantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries), consent.GrantedAt, consent.ExpiresAt, consent.RevokedAt);
}
