using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Developers;

// Trusted command boundary: callers authenticate live actors and recheck permission before commit.
// This service joins their transaction; it neither grants permission nor commits a command.
public sealed class MiniAppReviewAuditService(AppDbContext db)
{
    public static string Hash<T>(T payload) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload))).ToLowerInvariant();

    public async Task<MiniAppReviewAction?> FindAsync(MiniAppReviewAuditActor actor, Guid app, Guid request, string payloadHash, CancellationToken ct)
    {
        ValidateActor(actor);
        var previous = await db.MiniAppReviewActions.AsNoTracking().SingleOrDefaultAsync(x => x.ActorType == actor.Type && x.ActorId == actor.Id && x.RequestId == request, ct);
        if (previous != null && (previous.MiniAppId != app || previous.PayloadHash != payloadHash))
            throw new DeveloperAuthException(409, "REQUEST_CONFLICT", "Mã yêu cầu đã được dùng cho thao tác khác.");
        return previous;
    }

    public async Task<MiniAppReviewAction> AppendAsync(MiniAppReviewAuditEntry entry, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Review audit requires the command transaction.");
        ValidateActor(entry.Actor);
        if (entry.MiniAppId == Guid.Empty || entry.RequestId == Guid.Empty || entry.CorrelationId == Guid.Empty ||
            !Enum.IsDefined(entry.Action) || entry.PayloadHash is not { Length: 64 } ||
            entry.PayloadHash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            entry.BeforeState?.Length > 32 || entry.AfterState?.Length > 32 || entry.Reason?.Length > 2000) throw Invalid();
        ValidateJson(entry.MetadataJson, 2048); ValidateJson(entry.ResultJson, 4096);
        Guid? socialUser = null;
        if (entry.Actor.Type == MiniAppReviewActorType.Admin)
        {
            var admin = await db.Accounts.AsNoTracking().Where(x => x.Id == entry.Actor.Id && x.Role == AccountRole.Admin
                && x.Status == AccountStatus.Active && x.DeletedAt == null).Select(x => new { UserId = x.User == null ? (Guid?)null : x.User.Id }).SingleOrDefaultAsync(ct)
                ?? throw MiniAppReviewAccess.Forbidden();
            socialUser = admin.UserId;
        }
        var action = new MiniAppReviewAction
        {
            MiniAppId = entry.MiniAppId, MiniAppVersionId = entry.VersionId, ReviewRequestId = entry.ReviewId,
            ActorType = entry.Actor.Type, ActorId = entry.Actor.Id,
            ActorDeveloperId = entry.Actor.Type == MiniAppReviewActorType.Developer ? entry.Actor.Id : null,
            ActorAdminAccountId = entry.Actor.Type == MiniAppReviewActorType.Admin ? entry.Actor.Id : null,
            Action = entry.Action, RequestId = entry.RequestId, CorrelationId = entry.CorrelationId, PayloadHash = entry.PayloadHash,
            BeforeState = entry.BeforeState, AfterState = entry.AfterState, Reason = entry.Reason, MetadataJson = entry.MetadataJson, ResultJson = entry.ResultJson
        };
        db.MiniAppReviewActions.Add(action);
        if (socialUser is { } user)
            db.AdminLogs.Add(new() { AdminId = user, Action = "mini_app.review." + entry.Action, TargetType = "MiniApp", TargetId = entry.MiniAppId,
                Description = "Review action " + entry.Action + "; correlation " + entry.CorrelationId });
        await db.SaveChangesAsync(ct);
        return action;
    }

    private static void ValidateActor(MiniAppReviewAuditActor actor)
    {
        if (actor == null || actor.Id == Guid.Empty || !Enum.IsDefined(actor.Type)) throw Invalid();
    }

    private static void ValidateJson(string json, int maximum)
    {
        try
        {
            if (json == null || Encoding.UTF8.GetByteCount(json) is < 2 || Encoding.UTF8.GetByteCount(json) > maximum) throw Invalid();
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw Invalid();
            Walk(document.RootElement);
        }
        catch (JsonException) { throw Invalid(); }
    }

    private static void Walk(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                var key = property.Name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
                if (!keys.Add(property.Name) || key is "clientsecret" or "secretencrypted" or "password" or "passwordhash"
                    or "accesstoken" or "refreshtoken" or "authorization" or "cookie" or "token" or "credential") throw Invalid();
                Walk(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) Walk(item);
    }
    private static DeveloperAuthException Invalid() => new(400, "REVIEW_AUDIT_INVALID", "Dữ liệu nhật ký xét duyệt không hợp lệ.");
}
