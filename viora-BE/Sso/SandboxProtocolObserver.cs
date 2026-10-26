using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Sso;

// Separate scopes keep optional diagnostics failures out of OpenIddict's persistence context.
public sealed class SandboxProtocolObserver(IServiceScopeFactory scopes,ILogger<SandboxProtocolObserver> logger)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    private async Task Observe(Func<MiniAppSandboxProtocolService,Task> action)
    {
        try{
            await using var scope=scopes.CreateAsyncScope();
            if(scope.ServiceProvider.GetService<MiniAppSandboxProtocolService>() is {} service)await action(service);
        }catch(Exception){logger.LogWarning("Sandbox protocol observation unavailable.");}
    }
    public async Task<ClaimsPrincipal> BindAsync(ClaimsPrincipal principal,string? marker,string clientKey,CancellationToken ct)
    {await Observe(service=>service.BindAsync(principal,marker,clientKey,ct));return principal;}
    public async Task UserInfoAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        await Observe(service=>service.UserInfoAsync(principal,ct));
        await ObserveTest(principal,"userinfo",ct);
    }
    private async Task ObserveTest(ClaimsPrincipal principal,string stage,CancellationToken ct)
    {
        if (!principal.HasClaim(x => x.Type == SandboxTestService.SessionClaim)) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (scope.ServiceProvider.GetService<SandboxTestService>() is { } tests) await tests.RecordAsync(principal,stage,ct);
        }
        catch (Exception) { logger.LogWarning("Sandbox test observation unavailable."); }
    }
    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        if(!string.IsNullOrEmpty(context.AuthorizationCode)&&context.AuthorizationCodePrincipal is {} code)
        {
            await Observe(service=>service.RecordAsync(code,SandboxProtocolStage.Authorize,code.GetTokenId(),context.CancellationToken));
            await ObserveTest(code,"authorize",context.CancellationToken);
        }
        if(!string.IsNullOrEmpty(context.AccessToken)&&context.AccessTokenPrincipal is {} access&&context.Principal is {} principal)
        {
            await Observe(service=>service.RecordAsync(principal,SandboxProtocolStage.Token,access.GetTokenId(),context.CancellationToken));
            await ObserveTest(principal,"token",context.CancellationToken);
        }
    }
}
