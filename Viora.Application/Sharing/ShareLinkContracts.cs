namespace Viora.Application.Sharing;

public enum ShareLinkError
{
    NotFound,
    Forbidden,
    Invalid,
    Dissolved
}

public sealed record ShareLinkResult<T>(bool IsSuccess, T? Value, ShareLinkError? Error, string? Message)
{
    public static ShareLinkResult<T> Success(T value) => new(true, value, null, null);
    public static ShareLinkResult<T> Failure(ShareLinkError error, string message) => new(false, default, error, message);
}

public sealed record ShareLinkResponse(string ShareUrl);
public sealed record GroupShareLinkResponse(string InviteCode, string ShareUrl);

public interface IShareLinkService
{
    Task<ShareLinkResult<ShareLinkResponse>> GetUserShareLinkAsync(Guid viewerUserId, Guid userId, CancellationToken token);
    Task<ShareLinkResult<ShareLinkResponse>> GetPostShareLinkAsync(Guid viewerUserId, Guid postId, CancellationToken token);
    Task<ShareLinkResult<ShareLinkResponse>> GetReelShareLinkAsync(Guid viewerUserId, Guid reelId, CancellationToken token);
    Task<ShareLinkResult<GroupShareLinkResponse>> GetGroupShareLinkAsync(Guid viewerUserId, Guid groupId, CancellationToken token);
}
