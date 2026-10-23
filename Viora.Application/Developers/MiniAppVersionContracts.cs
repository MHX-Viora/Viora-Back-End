using System.Text.Json.Serialization;

namespace Viora.Application.Developers;

public sealed record MiniAppVersionSummaryDto(Guid Id, long VersionCode, string VersionName, string State,
    string SnapshotHash, Guid ConcurrencyToken, DateTime CreatedAt, DateTime UpdatedAt, DateTime? SubmittedAt);

public sealed record MiniAppVersionCommand(Guid RequestId, Guid ConcurrencyToken);
public sealed record MiniAppVersionCredentialDto(Guid VersionId, Guid ConcurrencyToken, string ClientId, string ClientSecret);

public sealed record MiniAppVersionPageDto(IReadOnlyList<MiniAppVersionSummaryDto> Items, int Page, int PageSize, int Total);
public sealed record MiniAppVersionDetailDto(MiniAppVersionSummaryDto Version, MiniAppVersionSnapshot Snapshot);
public sealed record MiniAppVersionCreateRequest(Guid RequestId, Guid AppVersion, string VersionName);
public sealed record MiniAppProductionIntentRequest(string ClientType, string[] Scopes, string[] RedirectUris);
public sealed record MiniAppVersionUpdateRequest(Guid RequestId, Guid ConcurrencyToken, string VersionName,
    [property: JsonRequired] bool SsoEnabled, MiniAppProductionIntentRequest? Production);
