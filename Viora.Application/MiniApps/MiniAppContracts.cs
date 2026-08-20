using Viora.Domain.Entities;

namespace Viora.Application.MiniApps;

public static class MiniAppErrorCodes
{
    public const string NotFound = "MINI_APP_NOT_FOUND";
    public const string NotActive = "MINI_APP_NOT_ACTIVE";
    public const string Suspended = "MINI_APP_SUSPENDED";
    public const string DomainNotAllowed = "MINI_APP_DOMAIN_NOT_ALLOWED";
    public const string ConsentRequired = "CONSENT_REQUIRED";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string InvalidClient = "INVALID_CLIENT";
    public const string InvalidLaunchCode = "INVALID_LAUNCH_CODE";
    public const string LaunchCodeExpired = "LAUNCH_CODE_EXPIRED";
    public const string LaunchCodeUsed = "LAUNCH_CODE_USED";
    public const string DeveloperSuspended = "DEVELOPER_SUSPENDED";
    public const string RateLimited = "RATE_LIMITED";
}

public sealed class MiniAppException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record MiniAppPermissionDto(string Code, string Name, string? Description, bool IsSensitive);
public sealed record MiniAppListItemDto(Guid Id, string Name, string Slug, string? Description, string? IconUrl, string? CoverUrl, bool IsFeatured);
public sealed record MiniAppListResponse(IReadOnlyList<MiniAppListItemDto> Items);
public sealed record MiniAppDetailDto(Guid Id, string Name, string Slug, string? Description, string? IconUrl, string? CoverUrl, string Developer, IReadOnlyList<MiniAppPermissionDto> Permissions, string Status);
public sealed record LaunchMiniAppResponse(bool RequiresConsent, IReadOnlyList<MiniAppPermissionDto> Permissions, string? LaunchUrl, int? ExpiresIn, IReadOnlyList<string>? AllowedDomains);
public sealed record GrantConsentRequest(IReadOnlyList<string> Permissions, bool Granted);
public sealed record ExchangeLaunchCodeRequest(string ClientId, string ClientSecret, string Code);
public sealed record ExchangeLaunchCodeResponse(string? Subject, string? DisplayName, string? AvatarUrl, string? Email, string? Phone, IReadOnlyList<string> Scopes);
public sealed record MiniAppProjectedProfile(string? Subject, string? DisplayName, string? AvatarUrl, string? Email, string? Phone, IReadOnlyList<string> Scopes);

public sealed record MiniAppConfigurationInput(
    string Name, string Slug, string? Description, string? IconUrl, string? CoverUrl,
    string WebUrl, string CallbackUrl, IReadOnlyList<string> AllowedDomains,
    IReadOnlyList<string> Permissions, bool IsFeatured = false);
public sealed record MiniAppCredentialResponse(Guid Id, string ClientId, string ClientSecret);
public sealed record MiniAppDeveloperView(
    Guid Id, string Name, string Slug, string Developer, string? Description, string? IconUrl, string? CoverUrl,
    string WebUrl, string CallbackUrl, IReadOnlyList<string> AllowedDomains,
    IReadOnlyList<MiniAppPermissionDto> Permissions, string ClientId, string Status,
    bool IsFeatured, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record DeveloperInput(string Name, string? CompanyName, string Email, string? Phone, string? Website, Guid? AccountId);
public sealed record DeveloperDto(Guid Id, Guid? AccountId, string Name, string? CompanyName, string Email, string? Phone, string? Website, string Status, int MiniAppCount, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record MiniAppAdminListItem(Guid Id, string Name, string Slug, string Developer, string WebUrl, string Status, bool IsFeatured, DateTime CreatedAt);
public sealed record MiniAppAdminDashboard(int TotalMiniApps, int Active, int PendingReview, int Suspended, int Developers, int SuccessfulLaunches, int FailedLaunches);
public sealed record MiniAppAdminPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record MiniAppAuditDto(Guid Id, Guid? MiniAppId, Guid? DeveloperId, Guid? ActorAccountId, string Action, string? Detail, DateTime CreatedAt);

public interface IMiniAppService
{
    Task<MiniAppListResponse> GetActiveAsync(CancellationToken cancellationToken);
    Task<MiniAppDetailDto?> GetActiveDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<LaunchMiniAppResponse> LaunchAsync(Guid accountId, Guid miniAppId, CancellationToken cancellationToken);
    Task GrantConsentAsync(Guid accountId, Guid miniAppId, GrantConsentRequest request, CancellationToken cancellationToken);
    Task RevokeConsentAsync(Guid accountId, Guid miniAppId, CancellationToken cancellationToken);
    Task<ExchangeLaunchCodeResponse> ExchangeAsync(ExchangeLaunchCodeRequest request, CancellationToken cancellationToken);
}

public interface IMiniAppManagementService
{
    Task<MiniAppAdminDashboard> GetDashboardAsync(CancellationToken cancellationToken);
    Task<MiniAppAdminPage<MiniAppAdminListItem>> GetAppsAsync(int page, int pageSize, string? search, MiniAppStatus? status, CancellationToken cancellationToken);
    Task<MiniAppDeveloperView?> GetAppAsync(Guid id, CancellationToken cancellationToken);
    Task SetAppStatusAsync(Guid actorAccountId, Guid id, MiniAppStatus status, string? reason, CancellationToken cancellationToken);
    Task UpdateAppAsync(Guid actorAccountId, Guid id, MiniAppConfigurationInput input, CancellationToken cancellationToken);
    Task<MiniAppAdminPage<DeveloperDto>> GetDevelopersAsync(int page, int pageSize, string? search, DeveloperStatus? status, CancellationToken cancellationToken);
    Task<DeveloperDto> CreateDeveloperAsync(Guid actorAccountId, DeveloperInput input, CancellationToken cancellationToken);
    Task SetDeveloperStatusAsync(Guid actorAccountId, Guid id, DeveloperStatus status, CancellationToken cancellationToken);
    Task<IReadOnlyList<MiniAppPermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken);
    Task<MiniAppAdminPage<MiniAppAuditDto>> GetAuditLogsAsync(int page, int pageSize, Guid? miniAppId, CancellationToken cancellationToken);
}

public interface IDeveloperMiniAppService
{
    Task<MiniAppCredentialResponse> CreateAsync(Guid accountId, MiniAppConfigurationInput input, CancellationToken cancellationToken);
    Task<IReadOnlyList<MiniAppDeveloperView>> GetOwnedAsync(Guid accountId, CancellationToken cancellationToken);
    Task<MiniAppDeveloperView?> GetOwnedAsync(Guid accountId, Guid id, CancellationToken cancellationToken);
    Task UpdateOwnedAsync(Guid accountId, Guid id, MiniAppConfigurationInput input, CancellationToken cancellationToken);
    Task SubmitReviewAsync(Guid accountId, Guid id, CancellationToken cancellationToken);
    Task<MiniAppCredentialResponse> RotateSecretAsync(Guid accountId, Guid id, CancellationToken cancellationToken);
    Task<MiniAppDeveloperView> GetPartnerConfigurationAsync(string clientId, string clientSecret, CancellationToken cancellationToken);
}

public interface IClientCredentialService
{
    string CreateClientId();
    string CreateSecret();
    string CreateLaunchCode();
    string HashSecret(string secret);
    bool VerifySecret(string secret, string hash);
    string HashLaunchCode(string code);
    string CreateSubject();
}
