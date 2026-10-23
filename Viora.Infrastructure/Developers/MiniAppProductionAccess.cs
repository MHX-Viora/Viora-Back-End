using Viora.Application.Developers;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Sso;

namespace Viora.Infrastructure.Developers;

public sealed record MiniAppProductionActor(Guid? AccountId, Guid? SourceSocialSessionId)
{
    public static readonly MiniAppProductionActor Anonymous = new(null, null);
}

public sealed class MiniAppProductionAccess(AppDbContext db, TimeProvider clock)
{
    public async Task ValidateAsync(MiniAppProductionActor actor, CancellationToken ct)
    {
        if (actor.AccountId == null && actor.SourceSocialSessionId == null) return;
        if (actor.AccountId is not { } account || account == Guid.Empty ||
            actor.SourceSocialSessionId is not { } source || source == Guid.Empty) throw Invalid();
        try { await new SsoSessionService(db, clock).ValidateSocialSessionAsync(account, source, ct); }
        catch (DeveloperAuthException) { throw Invalid(); }
    }
    public static DeveloperAuthException Invalid() => new(401, "HOST_SOURCE_INVALID", "Phiên ANKT không hợp lệ. Vui lòng đăng nhập lại.");
}
