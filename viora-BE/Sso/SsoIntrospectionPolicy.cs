using Microsoft.AspNetCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Viora.Application.Developers;
using Viora.Infrastructure.Sso;

namespace viora_BE.Sso;

// OpenIddict authenticates the confidential client and validates the token first.
public sealed class SsoIntrospectionPolicy(IOptions<SsoOptions> options, SsoClientReader clients, SsoTokenService tokens)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ValidateIntrospectionRequestContext>
{
    public async ValueTask HandleAsync(OpenIddictServerEvents.ValidateIntrospectionRequestContext context)
    {
        if (context.Transaction.GetHttpRequest()?.Headers.ContainsKey("Origin") != false ||
            !options.Value.IntrospectionClientIds.Contains(context.ClientId, StringComparer.Ordinal))
        {
            context.Reject(OpenIddictConstants.Errors.UnauthorizedClient);
            return;
        }
        try
        {
            var client = await clients.EligibleAsync(context.ClientId!, [OpenIddictConstants.Scopes.OpenId], null, context.CancellationToken);
            if (client.ClientType != "Confidential")
            {
                context.Reject(OpenIddictConstants.Errors.UnauthorizedClient);
                return;
            }
            if (context.GenericTokenPrincipal?.GetPresenters() is not { Length: 1 } presenters || presenters[0] != context.ClientId)
            {
                context.Reject(OpenIddictConstants.Errors.InvalidToken);
                return;
            }
            await tokens.RefreshClaimsAsync(context.GenericTokenPrincipal, null, context.CancellationToken);
        }
        catch (DeveloperAuthException) { context.Reject(OpenIddictConstants.Errors.InvalidToken); }
    }
}
