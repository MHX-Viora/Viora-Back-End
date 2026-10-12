namespace Viora.Application.MiniApps;
public sealed record DeveloperReviewHistoryDto(Guid Id, Guid ReviewerId, string PreviousStatus, string Status, string? Reason, DateTime CreatedAt, int Version, string? InternalNotes = null);
public sealed record DeveloperReviewInput(int Version, string Status, string? Reason = null, string? InternalNotes = null);
public sealed record DeveloperSubmitInput(int Version, string TermsVersion, bool AcceptedTerms);
public sealed record DeveloperTermsDocument(string Id, string Title, string Content);
public sealed record DeveloperTermsDto(string Version, IReadOnlyList<DeveloperTermsDocument> Documents, bool EmailVerificationRequired = true, bool PhoneVerificationRequired = false, bool LegalDocumentsRequired = false);
public sealed record DeveloperEmailRequest(string Email);
public sealed record DeveloperEmailConfirm(Guid VerificationId, string Code);
public sealed record DeveloperEmailChallenge(Guid VerificationId, DateTime ExpiresAt, int ResendAfterSeconds);
public sealed record DeveloperEmailConfirmed(bool Verified, string Email, DateTime VerifiedAt);
public interface IDeveloperVerificationMailer
{
    Task SendCodeAsync(string email, string code, CancellationToken ct);
}
public interface IDeveloperPlatformService
{
    DeveloperTermsDto GetTerms();
    Task<DeveloperProfileDto?> GetProfileAsync(Guid accountId, CancellationToken ct);
    Task<DeveloperProfileDto> SaveDraftAsync(Guid accountId, DeveloperInput input, CancellationToken ct);
    Task<DeveloperProfileDto> SubmitAsync(Guid accountId, DeveloperSubmitInput input, CancellationToken ct);
    Task<DeveloperEmailChallenge> RequestEmailAsync(Guid accountId, DeveloperEmailRequest input, CancellationToken ct);
    Task<DeveloperEmailConfirmed> ConfirmEmailAsync(Guid accountId, DeveloperEmailConfirm input, CancellationToken ct);
    Task<DeveloperProfileDto> GetAdminApplicationAsync(Guid id, CancellationToken ct);
    Task<DeveloperProfileDto> ReviewAsync(Guid actorAccountId, Guid id, DeveloperReviewInput input, CancellationToken ct);
}
