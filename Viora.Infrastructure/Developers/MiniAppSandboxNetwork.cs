using Viora.Application.Developers;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxNetwork(IPublicDnsResolver resolver)
{
    public async Task<string> ValidateExternalAsync(string? value,string partnerOrigin,CancellationToken ct,IReadOnlyCollection<string>? callbackUrls=null)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>2048||value.Any(char.IsControl)||value.Contains('\\')||
            !Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length!=0||
            uri.Query.Length!=0||uri.Fragment.Length!=0||uri.GetLeftPart(UriPartial.Authority)!=partnerOrigin)
            throw ExternalDenied();
        // Reject escaped delimiters/double encoding and auth/callback routes, including encoded names.
        var path=Uri.UnescapeDataString(uri.AbsolutePath);
        if(path.IndexOfAny(['%','\\','?','#',';'])>=0||path.Any(char.IsControl)||path.StartsWith("/.well-known",StringComparison.OrdinalIgnoreCase)||
            path.Split(['/', '.', '-', '_'],StringSplitOptions.RemoveEmptyEntries).Any(segment=>
                new[]{"auth","authorize","authorization","oauth","oauth2","oidc","connect","callback","signin","sign","login","logout","token","userinfo","sso"}
                    .Contains(segment,StringComparer.OrdinalIgnoreCase))||
            callbackUrls?.Any(callback=>Uri.TryCreate(callback,UriKind.Absolute,out var destination)&&destination.GetLeftPart(UriPartial.Authority)==partnerOrigin&&
                string.Equals(Uri.UnescapeDataString(destination.AbsolutePath),path,StringComparison.OrdinalIgnoreCase))==true)throw ExternalDenied();
        var normalized=uri.AbsoluteUri;
        await RequirePublicAsync(normalized,ct);return normalized;
    }
    private static DeveloperAuthException ExternalDenied()=>new(403,"SANDBOX_EXTERNAL_FORBIDDEN","Liên kết ngoài không được cho phép trong phiên Sandbox.");
    // Eligibility gate only: browser DNS and subsequent partner navigation remain browser-controlled.
    // This service never fetches arbitrary partner content or exposes DNS error details.
    public async Task RequirePublicAsync(string website,CancellationToken ct)
    {
        var normalized=PublicWebAddress.Normalize(website);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var answers=await resolver.ResolveAsync(new Uri(normalized).IdnHost,deadline.Token).WaitAsync(deadline.Token);
            if(answers.Length is 0 or >32 || answers.Any(ip=>!PublicNetwork.IsPublic(ip)))throw Unavailable();
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(DeveloperAuthException){throw;}
        catch{throw Unavailable();}
    }
    private static DeveloperAuthException Unavailable()=>new(403,"SANDBOX_NETWORK_UNAVAILABLE","Không xác nhận được domain công khai cho website Sandbox. Kiểm tra DNS rồi thử lại.");
}
