using Microsoft.EntityFrameworkCore;
using Viora.Application.GroupCalls;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.GroupCalls;

public sealed class GroupCallService(
    AppDbContext db,
    ILiveKitTokenIssuer tokenIssuer,
    IPushNotificationSender pushNotificationSender,
    IRealtimeService realtimeService) : IGroupCallService
{
    public const int MaximumParticipants = 25;

    public async Task<GroupCallResult<GroupCallJoinResponse>> StartAsync(
        Guid userId,
        StartGroupCallRequest request,
        CancellationToken token)
    {
        if (!Enum.IsDefined(request.CallType))
            return JoinFailure(GroupCallError.InvalidConversation, "Loại cuộc gọi không hợp lệ.");

        var access = await Access(userId, request.ConversationId, token);
        if (!access.IsSuccess || access.Value is null)
            return JoinFailure(access.Error!.Value, access.Message!);

        var call = await db.GroupCallSessions
            .Include(x => x.StartedByUser)
            .FirstOrDefaultAsync(
                x => x.ConversationId == request.ConversationId &&
                     x.Status == GroupCallStatus.Active,
                token);
        if (call is null)
        {
            var now = DateTime.UtcNow;
            db.Entry(access.Value).State = EntityState.Unchanged;
            call = new GroupCallSession
            {
                Id = Guid.NewGuid(),
                ConversationId = request.ConversationId,
                StartedByUserId = userId,
                StartedByUser = access.Value,
                CallType = request.CallType,
                Status = GroupCallStatus.Active,
                StartedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.GroupCallSessions.Add(call);
            await db.SaveChangesAsync(token);
        }
        await NotifyStarted(call, access.Value, token);
        return JoinResult(call, access.Value);
    }

    public async Task<GroupCallResult<GroupCallJoinResponse>> JoinAsync(
        Guid userId,
        Guid callId,
        CancellationToken token)
    {
        var call = await Find(callId, token);
        if (call is null || call.Status != GroupCallStatus.Active)
            return JoinFailure(GroupCallError.NotFound, "Cuộc gọi nhóm không còn hoạt động.");
        var access = await Access(userId, call.ConversationId, token);
        return !access.IsSuccess || access.Value is null
            ? JoinFailure(access.Error!.Value, access.Message!)
            : JoinResult(call, access.Value);
    }

    public async Task<GroupCallResult<GroupCallResponse>> GetAsync(
        Guid userId,
        Guid callId,
        CancellationToken token)
    {
        var call = await Find(callId, token);
        return call is null
            ? GroupCallResult<GroupCallResponse>.Failure(GroupCallError.NotFound, "Không tìm thấy cuộc gọi nhóm.")
            : await Authorized(userId, call, token);
    }

    public async Task<GroupCallResult<GroupCallResponse>> GetActiveAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken token)
    {
        var call = await db.GroupCallSessions.AsNoTracking()
            .Include(x => x.StartedByUser)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(
                x => x.ConversationId == conversationId &&
                     x.Status == GroupCallStatus.Active,
                token);
        return call is null
            ? GroupCallResult<GroupCallResponse>.Failure(GroupCallError.NotFound, "Nhóm chưa có cuộc gọi đang hoạt động.")
            : await Authorized(userId, call, token);
    }

    public async Task<GroupCallResult<GroupCallResponse>> EndAsync(
        Guid userId,
        Guid callId,
        CancellationToken token)
    {
        var call = await db.GroupCallSessions
            .Include(x => x.StartedByUser)
            .Include(x => x.Conversation).ThenInclude(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == callId, token);
        if (call is null)
            return GroupCallResult<GroupCallResponse>.Failure(GroupCallError.NotFound, "Không tìm thấy cuộc gọi nhóm.");
        var member = call.Conversation.Members.FirstOrDefault(
            x => x.UserId == userId && x.Status == ConversationMemberStatus.Active);
        if (member is null ||
            (call.StartedByUserId != userId &&
             member.Role is not (ConversationMemberRole.Owner or ConversationMemberRole.Admin)))
            return GroupCallResult<GroupCallResponse>.Failure(
                GroupCallError.Forbidden,
                "Chỉ người tạo hoặc quản trị viên được kết thúc cuộc gọi.");
        if (call.Status == GroupCallStatus.Active)
        {
            var now = DateTime.UtcNow;
            call.Status = GroupCallStatus.Ended;
            call.EndedAt = now;
            call.Duration = Math.Max(0, (int)(now - call.StartedAt).TotalSeconds);
            call.UpdatedAt = now;
            await db.SaveChangesAsync(token);
            await NotifyEnded(call, token);
        }
        return GroupCallResult<GroupCallResponse>.Success(Map(call));
    }

    private GroupCallResult<GroupCallJoinResponse> JoinResult(GroupCallSession call, User user)
    {
        try
        {
            var credentials = tokenIssuer.Issue(call.Id, user.Id, user.DisplayName, call.CallType);
            return GroupCallResult<GroupCallJoinResponse>.Success(
                new GroupCallJoinResponse(Map(call), credentials.LiveKitUrl, credentials.Token));
        }
        catch (LiveKitConfigurationException error)
        {
            return JoinFailure(GroupCallError.Configuration, error.Message);
        }
    }

    private async Task<GroupCallResult<User>> Access(
        Guid userId,
        Guid conversationId,
        CancellationToken token)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .Where(x => x.Id == conversationId)
            .Select(x => new
            {
                x.ConversationType,
                x.DeletedAt,
                MemberCount = x.Members.Count(m => m.Status == ConversationMemberStatus.Active),
                User = x.Members
                    .Where(m => m.UserId == userId && m.Status == ConversationMemberStatus.Active)
                    .Select(m => m.User)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(token);
        if (conversation is null || conversation.DeletedAt.HasValue ||
            conversation.ConversationType != ConversationType.Group)
            return GroupCallResult<User>.Failure(
                GroupCallError.InvalidConversation,
                "Cuộc gọi nhóm chỉ khả dụng trong nhóm đang hoạt động.");
        if (conversation.User is null)
            return GroupCallResult<User>.Failure(
                GroupCallError.Forbidden,
                "Bạn không phải thành viên đang hoạt động của nhóm.");
        if (conversation.MemberCount > MaximumParticipants)
            return GroupCallResult<User>.Failure(
                GroupCallError.TooManyParticipants,
                $"Cuộc gọi hỗ trợ tối đa {MaximumParticipants} thành viên.");
        return GroupCallResult<User>.Success(conversation.User);
    }

    private async Task<GroupCallResult<GroupCallResponse>> Authorized(
        Guid userId,
        GroupCallSession call,
        CancellationToken token) =>
        await db.ConversationMembers.AsNoTracking().AnyAsync(
            x => x.ConversationId == call.ConversationId &&
                 x.UserId == userId &&
                 x.Status == ConversationMemberStatus.Active,
            token)
            ? GroupCallResult<GroupCallResponse>.Success(Map(call))
            : GroupCallResult<GroupCallResponse>.Failure(
                GroupCallError.Forbidden,
                "Bạn không còn là thành viên của nhóm.");

    private Task<GroupCallSession?> Find(Guid id, CancellationToken token) =>
        db.GroupCallSessions.AsNoTracking()
            .Include(x => x.StartedByUser)
            .FirstOrDefaultAsync(x => x.Id == id, token);

    private static GroupCallResponse Map(GroupCallSession call) => new(
        call.Id,
        call.ConversationId,
        new GroupCallParticipantResponse(
            call.StartedByUserId,
            call.StartedByUser.DisplayName,
            call.StartedByUser.AvatarUrl),
        call.CallType,
        call.Status,
        call.StartedAt,
        call.EndedAt,
        call.Duration);

    private static GroupCallResult<GroupCallJoinResponse> JoinFailure(
        GroupCallError error,
        string message) => GroupCallResult<GroupCallJoinResponse>.Failure(error, message);

    private async Task NotifyStarted(
        GroupCallSession call,
        User inviter,
        CancellationToken token)
    {
        var recipients = await db.ConversationMembers.AsNoTracking()
            .Where(x => x.ConversationId == call.ConversationId &&
                        x.Status == ConversationMemberStatus.Active &&
                        x.UserId != inviter.Id)
            .Select(x => x.UserId)
            .ToListAsync(token);
        var invitation = new Dictionary<string, string>
        {
            ["type"] = "GroupCall",
            ["callId"] = call.Id.ToString(),
            ["conversationId"] = call.ConversationId.ToString(),
            ["callType"] = ((short)call.CallType).ToString(),
            ["isGroupCall"] = bool.TrueString.ToLowerInvariant(),
            ["callerId"] = inviter.Id.ToString(),
            ["callerDisplayName"] = inviter.DisplayName,
            ["callerAvatarUrl"] = Avatar(inviter)
        };
        await realtimeService.SendToUsersAsync(
            recipients,
            RealtimeEvents.GroupCallStarted,
            invitation,
            token);
        foreach (var recipient in recipients)
        {
            await pushNotificationSender.SendAsync(
                new PushMessage(
                    recipient,
                    inviter.DisplayName,
                    call.CallType == GroupCallType.Video
                        ? "Đang mời bạn tham gia cuộc gọi video nhóm"
                        : "Đang mời bạn tham gia cuộc gọi thoại nhóm",
                    invitation),
                token);
        }
    }

    private static string Avatar(User user) => user.AvatarUrl ?? string.Empty;

    private async Task NotifyEnded(GroupCallSession call, CancellationToken token)
    {
        var recipients = await db.ConversationMembers.AsNoTracking()
            .Where(x => x.ConversationId == call.ConversationId &&
                        x.Status == ConversationMemberStatus.Active)
            .Select(x => x.UserId)
            .ToListAsync(token);
        var ended = new Dictionary<string, string>
        {
            ["type"] = "GroupCallEnded",
            ["callId"] = call.Id.ToString(),
            ["conversationId"] = call.ConversationId.ToString()
        };
        await realtimeService.SendToUsersAsync(
            recipients,
            RealtimeEvents.GroupCallEnded,
            ended,
            token);
        foreach (var recipient in recipients)
        {
            await pushNotificationSender.SendAsync(
                new PushMessage(
                    recipient,
                    "Cuộc gọi nhóm đã kết thúc",
                    null,
                    ended),
                token);
        }
    }
}
