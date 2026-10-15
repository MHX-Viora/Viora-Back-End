namespace Viora.Application.MiniApps;

public sealed record DeveloperAccountVerificationDto(string? Email, DateTime? EmailVerifiedAt);
public interface IDeveloperAccountVerificationService
{
    Task<DeveloperAccountVerificationDto> GetAsync(Guid accountId, CancellationToken ct);
    Task<DeveloperAccountVerificationDto> VerifyEmailAsync(Guid accountId, string firebaseToken, CancellationToken ct);
}
