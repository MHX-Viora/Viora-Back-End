namespace Viora.Application.Developers;

public sealed record MiniAppHostPrepareDto(string BindingId, string BrowserNonce, DateTime ExpiresAt)
{
    public override string ToString() => "Mini App host preparation; browser credentials redacted";
}
public sealed record MiniAppLaunchRequest(Guid RequestId, string Platform, string BindingId, string BrowserNonce)
{
    public override string ToString() => $"Mini App launch request {RequestId}; browser binding redacted";
    public void Validate()
    {
        if (RequestId == Guid.Empty || Platform is not ("Web" or "Android" or "iOS") || BindingId is not { Length: > 0 and <= 4096 } ||
            BrowserNonce is not { Length: 43 } || BrowserNonce.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new DeveloperAuthException(400, "INVALID_LAUNCH", "Yêu cầu mở Mini App không hợp lệ.");
    }
}
public sealed record MiniAppLaunchReceiptDto(Guid SessionId, Guid PublicationId, string Version, string HostOrigin,
    string HostUrl, DateTime ExpiresAt, string Grant, IReadOnlyList<string> Capabilities, Guid CorrelationId)
{
    public override string ToString() => $"Mini App launch receipt {SessionId}; grant redacted";
}
public sealed record MiniAppRedeemRequest(string Grant, string BrowserNonce)
{
    public override string ToString() => "Mini App redemption; grant and browser nonce redacted";
}
public sealed record MiniAppHostActionRequest;
public sealed record MiniAppProductionEventRequest(string Code);
public sealed record MiniAppProductionContextDto(string AppId, string Environment = "Production", string Runtime = "controlled-web");
public sealed record MiniAppProductionDocumentDto(Guid SessionId, string AppId, string Name, string WebsiteUrl,
    string PartnerOrigin, bool SsoEnabled, DateTime ExpiresAt);
public sealed record MiniAppProductionExternalLinkDto(string Url, DateTime ExpiresAt);
public sealed record MiniAppProductionRedeemedDto(Guid SessionId, DateTime ExpiresAt, MiniAppPublicItemDto App,
    string WebsiteUrl, string PartnerOrigin, MiniAppProductionContextDto Context, IReadOnlyList<string> Capabilities, int HeartbeatSeconds);
