namespace Viora.Application.MiniApps;

public sealed record DeveloperReauthInput(string Password, string Operation, Guid? MiniAppId = null);
public sealed record DeveloperReauthResponse(string Proof, DateTime ExpiresAt);
public interface IDeveloperReauthenticationService
{
    Task<DeveloperReauthResponse> VerifyAsync(Guid accountId, DeveloperReauthInput input, CancellationToken ct);
    Task ConsumeAsync(Guid accountId, string operation, Guid? miniAppId, string? proof, CancellationToken ct);
}
