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

public sealed record MiniAppPermissionDto(string Code, string Name, string? Description, bool IsSensitive, bool IsActive = true);
public sealed record MiniAppListItemDto(Guid Id, string Name, string Slug, string? Description, string? IconUrl, string? CoverUrl, bool IsFeatured, string Developer = "", Guid? CategoryId = null, string? Category = null, string AuthenticationMode = "AnktSso", int PublishedVersion = 0);
public sealed record MiniAppListResponse(IReadOnlyList<MiniAppListItemDto> Items);
public sealed record MiniAppDetailDto(Guid Id, string Name, string Slug, string? Description, string? IconUrl, string? CoverUrl, string Developer, IReadOnlyList<MiniAppPermissionDto> Permissions, string Status, string WebUrl = "", IReadOnlyList<string>? AllowedDomains = null, IReadOnlyList<string>? AllowedOrigins = null, IReadOnlyList<string>? CallbackUrls = null, Guid? CategoryId = null, string? Category = null, string AuthenticationMode = "AnktSso", int PublishedVersion = 0);
public sealed record LaunchMiniAppResponse(bool RequiresConsent, IReadOnlyList<MiniAppPermissionDto> Permissions, string? LaunchUrl, int? ExpiresIn, IReadOnlyList<string>? AllowedDomains, Guid? SessionId = null, string? SessionToken = null);
public sealed record GrantConsentRequest(IReadOnlyList<string> Permissions, bool Granted);
public sealed record ExchangeLaunchCodeRequest(string ClientId, string? ClientSecret, string Code, string? RedirectUri = null, string? State = null, string? CodeVerifier = null);
public sealed record ValidateMiniAppSessionRequest(Guid SessionId, string SessionToken);
public sealed record ValidateMiniAppSessionResponse(bool Active, IReadOnlyList<string> Permissions, DateTime? ExpiresAt);
public sealed record AuthorizeMiniAppRequest(Guid SessionId, string SessionToken, string RedirectUri, string State, string CodeChallenge, string CodeChallengeMethod);
public sealed record ExchangeLaunchCodeResponse(string? Subject, string? DisplayName, string? AvatarUrl, string? Email, string? Phone, IReadOnlyList<string> Scopes);
public sealed record MiniAppProjectedProfile(string? Subject, string? DisplayName, string? AvatarUrl, string? Email, string? Phone, IReadOnlyList<string> Scopes);

public sealed record MiniAppConfigurationInput(
    string Name, string Slug, string? Description, string? IconUrl, string? CoverUrl,
    string WebUrl, string? CallbackUrl, IReadOnlyList<string> AllowedDomains,
    IReadOnlyList<string> Permissions, bool IsFeatured = false, Guid? CategoryId = null,
    string AuthenticationMode = "Independent", IReadOnlyList<string>? CallbackUrls = null,
    IReadOnlyList<string>? AllowedOrigins = null, string ClientAuthenticationMethod = "ClientSecretPost", int? Version = null);
public sealed record MiniAppCredentialResponse(Guid Id, string ClientId, string ClientSecret);
public sealed record MiniAppDeveloperView(
    Guid Id, string Name, string Slug, string Developer, string? Description, string? IconUrl, string? CoverUrl,
    string WebUrl, string CallbackUrl, IReadOnlyList<string> AllowedDomains,
    IReadOnlyList<MiniAppPermissionDto> Permissions, string ClientId, string Status,
    bool IsFeatured, DateTime CreatedAt, DateTime UpdatedAt, Guid? CategoryId = null, string? Category = null,
    string AuthenticationMode = "AnktSso", IReadOnlyList<string>? CallbackUrls = null,
    IReadOnlyList<string>? AllowedOrigins = null, string ClientAuthenticationMethod = "ClientSecretPost",
    int PublishedVersion = 0, int? PendingVersion = null);
public sealed record DeveloperInput(string Name, string? CompanyName, string Email, string? Phone, string? Website, Guid? AccountId);
public sealed record DeveloperDto(Guid Id, Guid? AccountId, string Name, string? CompanyName, string Email, string? Phone, string? Website, string Status, int MiniAppCount, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record MiniAppAdminListItem(Guid Id, string Name, string Slug, string Developer, string WebUrl, string Status, bool IsFeatured, DateTime CreatedAt, int? PendingVersion = null);
public sealed record MiniAppAdminDashboard(int TotalMiniApps, int Active, int PendingReview, int Suspended, int Developers, int SuccessfulLaunches, int FailedLaunches);
public sealed record MiniAppAdminPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record MiniAppAuditDto(Guid Id, Guid? MiniAppId, Guid? DeveloperId, Guid? ActorAccountId, string Action, string? Detail, DateTime CreatedAt);
public sealed record DeveloperProfileDto(Guid Id, Guid? AccountId, string Name, string? CompanyName, string Email, string? Phone, string? Website, string Status, int MiniAppCount, DateTime CreatedAt, DateTime UpdatedAt, string MembershipRole);
public sealed record DeveloperTeamInput(Guid AccountId, string Role = "Member");
public sealed record DeveloperTeamDto(Guid Id, Guid AccountId, string Role, DateTime CreatedAt);
public sealed record MiniAppCategoryDto(Guid Id, string Name, string Slug, bool IsActive = true);
public sealed record MiniAppCategoryInput(string Name, string Slug, bool IsActive = true);
public sealed record MiniAppPermissionInput(string Code, string Name, string? Description, bool IsSensitive, bool IsActive = true);
public sealed record MiniAppDomainInput(string Host);
public sealed record MiniAppDomainDto(Guid Id, string Host, DateTime? VerifiedAt, string ChallengeToken);
public sealed record MiniAppVersionDto(Guid Id, int Version, string Status, string? Reason, DateTime CreatedAt, DateTime? ReviewedAt, MiniAppConfigurationInput Configuration);
public sealed record MiniAppUsageDay(string Date, int Launches, int Failures);
public sealed record MiniAppAnalyticsDto(int SuccessfulLaunches, int FailedLaunches, int UniqueUsers, IReadOnlyList<MiniAppUsageDay> Daily);
public sealed record MiniAppReportInput(string Reason);
public sealed record MiniAppReportDto(Guid Id, Guid MiniAppId, string AppName, string Reason, string? Resolution, DateTime CreatedAt, DateTime? ResolvedAt);

public interface IMiniAppService
{
    Task<MiniAppListResponse> GetActiveAsync(CancellationToken cancellationToken);
    Task<MiniAppDetailDto?> GetActiveDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<LaunchMiniAppResponse> LaunchAsync(Guid accountId, Guid miniAppId, CancellationToken cancellationToken);
    Task GrantConsentAsync(Guid accountId, Guid miniAppId, GrantConsentRequest request, CancellationToken cancellationToken);
    Task RevokeConsentAsync(Guid accountId, Guid miniAppId, CancellationToken cancellationToken);
    Task<ExchangeLaunchCodeResponse> ExchangeAsync(ExchangeLaunchCodeRequest request, CancellationToken cancellationToken);
    Task<ValidateMiniAppSessionResponse> ValidateSessionAsync(Guid accountId, Guid miniAppId, ValidateMiniAppSessionRequest request, CancellationToken cancellationToken);
    Task CloseSessionAsync(Guid accountId, Guid miniAppId, ValidateMiniAppSessionRequest request, CancellationToken cancellationToken);
    Task<LaunchMiniAppResponse> AuthorizeAsync(Guid accountId, Guid miniAppId, AuthorizeMiniAppRequest request, CancellationToken cancellationToken);
    Task ReportAsync(Guid accountId, Guid miniAppId, MiniAppReportInput input, CancellationToken cancellationToken);
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
    Task<IReadOnlyList<MiniAppPermissionDto>> GetAdminPermissionsAsync(CancellationToken cancellationToken);
    Task<MiniAppAdminPage<MiniAppAuditDto>> GetAuditLogsAsync(int page, int pageSize, Guid? miniAppId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MiniAppCategoryDto>> GetCategoriesAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<MiniAppCategoryDto> SaveCategoryAsync(Guid actorAccountId, Guid? id, MiniAppCategoryInput input, CancellationToken cancellationToken);
    Task SavePermissionAsync(Guid actorAccountId, string? code, MiniAppPermissionInput input, CancellationToken cancellationToken);
    Task<MiniAppAdminPage<MiniAppReportDto>> GetReportsAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task ResolveReportAsync(Guid actorAccountId, Guid id, string reason, CancellationToken cancellationToken);
    Task<IReadOnlyList<MiniAppVersionDto>> GetVersionsAsync(Guid id, CancellationToken cancellationToken);
    Task<MiniAppCredentialResponse> CreateAdminAppAsync(Guid actorAccountId, Guid developerId, MiniAppConfigurationInput input, CancellationToken cancellationToken);
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
    Task<DeveloperProfileDto?> GetProfileAsync(Guid accountId, CancellationToken cancellationToken);
    Task<DeveloperProfileDto> RegisterDeveloperAsync(Guid accountId, DeveloperInput input, CancellationToken cancellationToken);
    Task<DeveloperProfileDto> UpdateProfileAsync(Guid accountId, DeveloperInput input, CancellationToken cancellationToken);
    Task<IReadOnlyList<DeveloperTeamDto>> GetTeamAsync(Guid accountId, CancellationToken cancellationToken);
    Task<DeveloperTeamDto> AddTeamAsync(Guid accountId, DeveloperTeamInput input, CancellationToken cancellationToken);
    Task RemoveTeamAsync(Guid accountId, Guid membershipId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MiniAppDomainDto>> GetDomainsAsync(Guid accountId, Guid id, CancellationToken cancellationToken);
    Task<MiniAppDomainDto> AddDomainAsync(Guid accountId, Guid id, MiniAppDomainInput input, CancellationToken cancellationToken);
    Task<MiniAppDomainDto> VerifyDomainAsync(Guid accountId, Guid id, Guid domainId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MiniAppVersionDto>> GetOwnedVersionsAsync(Guid accountId, Guid id, CancellationToken cancellationToken);
    Task<MiniAppAnalyticsDto> GetAnalyticsAsync(Guid accountId, Guid id, CancellationToken cancellationToken);
    Task<MiniAppAdminPage<MiniAppAuditDto>> GetOwnedAuditAsync(Guid accountId, Guid id, int page, int pageSize, CancellationToken cancellationToken);
}

public interface IMiniAppDomainVerifier
{
    Task<bool> VerifyAsync(string host, string challengeToken, CancellationToken cancellationToken);
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
