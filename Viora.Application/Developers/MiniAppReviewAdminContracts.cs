namespace Viora.Application.Developers;

public sealed record MiniAppReviewAdminQuery(string? Search, string? Status, Guid? Assignee, string? Sort, int Page = 1, int PageSize = 20);
public sealed record MiniAppReviewAdminListItem(MiniAppReviewSummaryDto Review, string AppId, string AppName, string VersionName,
    string DeveloperName, string DeveloperEmail, Guid? AssignedAdminAccountId, string? LogoUrl);
public sealed record MiniAppReviewAdminPageDto(IReadOnlyList<MiniAppReviewAdminListItem> Items, int Page, int PageSize, int Total);
public sealed record MiniAppReviewAdminOverviewDto(IReadOnlyDictionary<string, int> Reviews, int PublishedApps, int SuspendedApps);
public sealed record MiniAppReviewAdminDetailDto(MiniAppReviewDetailDto Review, string AppId, string DeveloperName, string DeveloperEmail,
    Guid? AssignedAdminAccountId, bool CanStart, bool CanAssign, bool CanDecide, bool CanSuspend);
public sealed record MiniAppAdminAppSummaryDto(string AppId, string Name, string DeveloperName, string DeveloperEmail,
    string LegacyStatus, string OperationalStatus, Guid AppVersion, Guid? OperationalRevision, Guid? PublicationId, Guid? PublishedVersionId,
    bool CanSuspend, bool CanRestore, string? OperationalReason);
public sealed record MiniAppAdminAppPageDto(IReadOnlyList<MiniAppAdminAppSummaryDto> Items, int Page, int PageSize, int Total);
public sealed record MiniAppAdminPublicationDto(Guid Id, Guid VersionId, Guid ReviewId, string Status, DateTime PublishedAt, DateTime? UnpublishedAt, Guid ConcurrencyToken, string? SuspensionReason);
public sealed record MiniAppAdminAppDetailDto(MiniAppAdminAppSummaryDto App, IReadOnlyList<MiniAppVersionSummaryDto> Versions,
    IReadOnlyList<MiniAppAdminPublicationDto> Publications, MiniAppVersionSnapshot? PublishedSnapshot);
