using Viora.Application.Developers;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxNetwork(IPublicDnsResolver resolver)
{
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
