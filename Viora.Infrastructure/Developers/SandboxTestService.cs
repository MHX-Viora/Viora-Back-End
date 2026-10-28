using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

public sealed class SandboxTestService(AppDbContext db, IOptions<SandboxTestOptions> configured, TimeProvider clock)
{
    public bool Enabled => Options.Enabled;
    private SandboxTestOptions Options => configured.Value;
    private static readonly SemaphoreSlim TemplateGate = new(1, 1);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static DeveloperAuthException Denied() => new(403, "SANDBOX_TEST_UNAVAILABLE", "Phiên kiểm thử không còn hợp lệ hoặc không thuộc ứng dụng này.");

    public async Task<MiniApp> OwnerAsync(Guid owner, Guid sourceSession, string appId, CancellationToken ct)
    {
        var now = Now;
        if (!await db.DeveloperSessions.AnyAsync(x => x.Id == sourceSession && x.DeveloperAccountId == owner && x.RevokedAt == null &&
            x.ExpiresAt > now && x.AbsoluteExpiresAt > now && x.Account.Status == DeveloperStatus.Active &&
            (x.Account.LockedUntil == null || x.Account.LockedUntil <= now), ct)) throw Denied();
        return await db.MiniApps.AsNoTracking().SingleOrDefaultAsync(x => x.AppId == appId && x.DeveloperAccountId == owner &&
            x.DeletedAt == null && x.Status != MiniAppStatus.Suspended && x.Status != MiniAppStatus.Rejected, ct)
            ?? throw new DeveloperAuthException(404, "SANDBOX_TEST_NOT_FOUND", "Không tìm thấy Mini App được phép kiểm thử.");
    }

    private async Task<SandboxIdentityTemplate> TemplateAsync(CancellationToken ct)
    {
        await TemplateGate.WaitAsync(ct);
        try { return await LoadTemplateAsync(ct); }
        finally { TemplateGate.Release(); }
    }

    private async Task<SandboxIdentityTemplate> LoadTemplateAsync(CancellationToken ct)
    {
        if (!Enabled) throw Denied();
        var template = await db.SandboxIdentityTemplates.IgnoreQueryFilters().Include(x => x.Account).ThenInclude(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == SandboxIdentityTemplate.DefaultId, ct);
        if (template != null)
        {
            if (!template.Account.IsSandbox || template.Account.User == null || template.Account.Role != AccountRole.User ||
                template.Account.Status != AccountStatus.Active || template.Account.DeletedAt != null) throw Denied();
            var changed = false;
            if (template.Account.User.AvatarUrl == null && SandboxTestOptions.CloudinaryUrl(Options.AvatarUrl))
            { template.Account.User.AvatarUrl = Options.AvatarUrl; changed = true; }
            if (template.Account.User.CoverUrl == null && SandboxTestOptions.CloudinaryUrl(Options.CoverUrl))
            { template.Account.User.CoverUrl = Options.CoverUrl; changed = true; }
            if (changed) await db.SaveChangesAsync(ct);
            return template;
        }
        var account = await db.Accounts.IgnoreQueryFilters().Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Email == "sandbox-test@domain.test", ct);
        // A reserved email alone is not proof that this is a test identity.
        if (account != null && (!account.IsSandbox || account.User == null || account.Role != AccountRole.User ||
            account.Status != AccountStatus.Active || account.DeletedAt != null))
            throw new DeveloperAuthException(409, "SANDBOX_TEMPLATE_CONFLICT", "Email mẫu đã được sử dụng; không thay đổi tài khoản hiện có.");
        account ??= new Account { Email = "sandbox-test@domain.test", IsSandbox = true, Status = AccountStatus.Active };
        account.User ??= new User { DisplayName = "ANKT Sandbox User", Gender = Gender.Male, IsVerified = true,
            AvatarUrl = SandboxTestOptions.CloudinaryUrl(Options.AvatarUrl) ? Options.AvatarUrl : null,
            CoverUrl = SandboxTestOptions.CloudinaryUrl(Options.CoverUrl) ? Options.CoverUrl : null };
        if (account.User.AvatarUrl == null && SandboxTestOptions.CloudinaryUrl(Options.AvatarUrl)) account.User.AvatarUrl = Options.AvatarUrl;
        if (account.User.CoverUrl == null && SandboxTestOptions.CloudinaryUrl(Options.CoverUrl)) account.User.CoverUrl = Options.CoverUrl;
        template = new SandboxIdentityTemplate { Id = SandboxIdentityTemplate.DefaultId, Account = account };
        db.SandboxIdentityTemplates.Add(template); await db.SaveChangesAsync(ct); return template;
    }

    public Task<SandboxIdentityTemplate> BridgeIdentityAsync(CancellationToken ct) => TemplateAsync(ct);

}
