using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

internal static class DeveloperOrganizationSelection
{
    public static async Task<Developer?> Resolve(AppDbContext db, Guid accountId, Guid? selected, bool ownerOnly, CancellationToken ct)
    {
        var query = db.Developers.Where(d => d.AccountId == accountId || (!ownerOnly && db.DeveloperMemberships.Any(m => m.DeveloperId == d.Id && m.AccountId == accountId)));
        if (selected is Guid id)
            return await query.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new MiniAppException("ORGANIZATION_ACCESS_DENIED", "No access to the selected organization.", 403);
        // Deterministic legacy default: own profile first, then oldest membership.
        return await query.OrderByDescending(d => d.AccountId == accountId).ThenBy(d => d.CreatedAt).ThenBy(d => d.Id).FirstOrDefaultAsync(ct);
    }
}
