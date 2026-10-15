using Viora.Application.MiniApps;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.MiniApps;

public static class MiniAppSecretPolicy
{
    public static bool Verify(IClientCredentialService credentials, MiniApp app, string secret, DateTime now)
        => credentials.VerifySecret(secret, app.ClientSecretHash) ||
           app.PreviousClientSecretExpiresAt > now && app.PreviousClientSecretHash is string previous && credentials.VerifySecret(secret, previous);
}
