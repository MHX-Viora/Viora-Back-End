using Viora.Application.Developers;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppProductionExternalLinkService(MiniAppProductionRuntimeService runtime, IPublicWebFetcher fetcher, TimeProvider clock)
{
    // No caller-selected URL: host chrome can only open the reviewed website.
    public async Task<MiniAppProductionExternalLinkDto> PrepareAsync(Guid id, string? credential, string? nonce, string? browser, CancellationToken ct)
    {
        var before = await runtime.ExternalTargetAsync(id, credential, nonce, browser, ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try { await fetcher.FetchAsync(new Uri(before.WebsiteUrl), 256 * 1024, false, deadline.Token); }
        catch (PublicFetchException) { throw Unavailable(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
        var after = await runtime.ExternalTargetAsync(id, credential, nonce, browser, ct);
        if (before.WebsiteUrl != after.WebsiteUrl || before.AppId != after.AppId || before.SessionId != after.SessionId)
            throw MiniAppProductionPolicy.Ended();
        return new(after.WebsiteUrl, new[] { clock.GetUtcNow().UtcDateTime.AddSeconds(15), after.ExpiresAt }.Min());
    }
    private static DeveloperAuthException Unavailable() => new(403, "EXTERNAL_LINK_DENIED", "Không thể xác nhận liên kết ngoài lúc này.");
}
