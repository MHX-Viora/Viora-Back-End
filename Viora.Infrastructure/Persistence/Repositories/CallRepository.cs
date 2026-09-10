using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Viora.Application.Calls;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class CallRepository(AppDbContext dbContext, ILogger<CallRepository> logger) : ICallRepository
{
    public async Task<CallResult<CallSessionResponse>> CreateCallAsync(CreateCallCommand command, CancellationToken cancellationToken)
    {
        var validation = await ValidateCallableConversationAsync(command.ConversationId, command.CallerId, cancellationToken);
        if (!validation.IsSuccess || validation.Value is null)
        {
            return CallResult<CallSessionResponse>.Failure(validation.Error ?? CallError.Validation, validation.Message ?? "Không thể gọi.");
        }

        var now = DateTime.UtcNow;
        var call = new CallSession
        {
            ConversationId = command.ConversationId,
            CallerId = command.CallerId,
            ReceiverId = validation.Value.ReceiverId,
            CallType = command.CallType,
            Status = CallStatus.Calling,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.CallSessions.Add(call);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Call Started. CallId: {CallId}, ConversationId: {ConversationId}.", call.Id, call.ConversationId);
        return await GetByIdAsync(command.CallerId, call.Id, cancellationToken);
    }

    public async Task<CallResult<CallSessionResponse>> AcceptCallAsync(
        AcceptCallCommand command,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.CallSessions
            .AsNoTracking()
            .Where(call => call.Id == command.CallId)
            .Select(call => new { call.ReceiverId, call.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return CallResult<CallSessionResponse>.Failure(CallError.NotFound, "Không tìm thấy cuộc gọi.");
        }
        if (current.ReceiverId != command.UserId)
        {
            return CallResult<CallSessionResponse>.Failure(CallError.Forbidden, "Chỉ người nhận mới được thao tác.");
        }
        if (current.Status != CallStatus.Calling)
        {
            return CallResult<CallSessionResponse>.Failure(CallError.InvalidState, "Cuộc gọi đã được xử lý trên thiết bị khác.");
        }

        var now = DateTime.UtcNow;
        var updated = await dbContext.CallSessions
            .Where(call => call.Id == command.CallId && call.Status == CallStatus.Calling)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(call => call.Status, CallStatus.Accepted)
                .SetProperty(call => call.AnsweredAt, now)
                .SetProperty(call => call.UpdatedAt, now), cancellationToken);
        if (updated != 1)
        {
            return CallResult<CallSessionResponse>.Failure(CallError.InvalidState, "Cuộc gọi đã được xử lý trên thiết bị khác.");
        }

        return await GetByIdAsync(command.UserId, command.CallId, cancellationToken);
    }

    public Task<CallResult<CallSessionResponse>> RejectCallAsync(RejectCallCommand command, CancellationToken cancellationToken) =>
        TransitionAsync(command.CallId, command.UserId, [CallStatus.Calling], CallStatus.Rejected, requireReceiver: true, cancellationToken: cancellationToken);

    public Task<CallResult<CallSessionResponse>> CancelCallAsync(CancelCallCommand command, CancellationToken cancellationToken) =>
        TransitionAsync(command.CallId, command.UserId, [CallStatus.Calling], CallStatus.Cancelled, requireCaller: true, cancellationToken: cancellationToken);

    public async Task<CallResult<CallSessionResponse>> EndCallAsync(EndCallCommand command, CancellationToken cancellationToken)
    {
        var current = await dbContext.CallSessions
            .AsNoTracking()
            .Where(call => call.Id == command.CallId)
            .Select(call => new
            {
                call.AnsweredAt,
                call.CallerId,
                call.ReceiverId,
                call.Status
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null) return CallResult<CallSessionResponse>.Failure(CallError.NotFound, "Không tìm thấy cuộc gọi.");
        if (current.CallerId != command.UserId && current.ReceiverId != command.UserId)
        {
            return CallResult<CallSessionResponse>.Failure(CallError.Forbidden, "Bạn không có quyền thao tác cuộc gọi này.");
        }
        if (IsTerminal(current.Status))
        {
            return CallResult<CallSessionResponse>.Failure(CallError.InvalidState, "Cuộc gọi đã kết thúc.");
        }

        var nextStatus = current.Status switch
        {
            CallStatus.Accepted => CallStatus.Ended,
            CallStatus.Calling when current.CallerId == command.UserId => CallStatus.Cancelled,
            CallStatus.Calling => CallStatus.Rejected,
            _ => (CallStatus?)null
        };
        if (nextStatus is null)
        {
            return CallResult<CallSessionResponse>.Failure(CallError.InvalidState, "Trạng thái cuộc gọi không hợp lệ.");
        }

        var now = DateTime.UtcNow;
        var duration = current.AnsweredAt.HasValue
            ? Math.Max(0, (int)(now - current.AnsweredAt.Value).TotalSeconds)
            : 0;
        var updated = await dbContext.CallSessions
            .Where(call => call.Id == command.CallId && call.Status == current.Status)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(call => call.Status, nextStatus.Value)
                .SetProperty(call => call.EndedAt, now)
                .SetProperty(call => call.Duration, duration)
                .SetProperty(call => call.UpdatedAt, now), cancellationToken);
        if (updated != 1)
        {
            return CallResult<CallSessionResponse>.Failure(CallError.InvalidState, "Cuộc gọi đã được xử lý trên thiết bị khác.");
        }

        return await GetByIdAsync(command.UserId, command.CallId, cancellationToken);
    }

    public Task<CallResult<CallSessionResponse>> MarkMissedAsync(Guid callId, CancellationToken cancellationToken) =>
        TransitionAsync(callId, null, [CallStatus.Calling], CallStatus.Missed, cancellationToken: cancellationToken);

    public async Task<CallHistoryResponse> GetHistoryAsync(GetCallHistoryQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
        var calls = dbContext.CallSessions
            .AsNoTracking()
            .Where(call => call.CallerId == query.UserId || call.ReceiverId == query.UserId);
        var totalItems = await calls.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var pageCalls = await calls
            .OrderByDescending(call => call.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = pageCalls.Select(Map).ToList();
        return new CallHistoryResponse(page, pageSize, totalItems, totalPages, items);
    }

    public async Task<CallResult<CallSessionResponse>> GetByIdAsync(Guid userId, Guid callId, CancellationToken cancellationToken)
    {
        var call = await dbContext.CallSessions
            .AsNoTracking()
            .Include(value => value.Caller)
            .Include(value => value.Receiver)
            .FirstOrDefaultAsync(value => value.Id == callId, cancellationToken);
        if (call is null)
        {
            return CallResult<CallSessionResponse>.Failure(CallError.NotFound, "Không tìm thấy cuộc gọi.");
        }
        if (call.CallerId != userId && call.ReceiverId != userId)
        {
            return CallResult<CallSessionResponse>.Failure(CallError.Forbidden, "Bạn không có quyền xem cuộc gọi này.");
        }
        return CallResult<CallSessionResponse>.Success(Map(call));
    }

    public async Task<CallParticipantRouting?> GetParticipantRoutingAsync(Guid userId, Guid callId, CancellationToken cancellationToken)
    {
        return await dbContext.CallSessions
            .AsNoTracking()
            .Where(call => call.Id == callId && (call.CallerId == userId || call.ReceiverId == userId))
            .Select(call => new CallParticipantRouting(
                call.Id,
                call.ConversationId,
                call.CallerId,
                call.ReceiverId,
                call.CallerId == userId ? call.ReceiverId : call.CallerId,
                call.Status))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CallResult<CallableConversation>> ValidateCallableConversationAsync(Guid conversationId, Guid callerId, CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations
            .AsNoTracking()
            .Where(value => value.Id == conversationId)
            .Select(value => new
            {
                value.ConversationType,
                value.DeletedAt,
                Members = value.Members
                    .Where(member => member.Status == ConversationMemberStatus.Active)
                    .Select(member => new
                    {
                        member.UserId,
                        AccountStatus = member.User.Account.Status,
                        member.User.Account.DeletedAt
                    })
                    .ToList(),
                IsBlocked = dbContext.ConversationBlocks.Any(block => block.ConversationId == conversationId)
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (conversation is null) return CallResult<CallableConversation>.Failure(CallError.ConversationNotFound, "Không tìm thấy cuộc trò chuyện.");
        if (conversation.DeletedAt.HasValue) return CallResult<CallableConversation>.Failure(CallError.ConversationDissolved, "Conversation has been dissolved.");
        if (conversation.ConversationType != ConversationType.Private) return CallResult<CallableConversation>.Failure(CallError.Validation, "Chỉ hỗ trợ gọi 1-1 trong phòng chat riêng.");
        if (conversation.IsBlocked) return CallResult<CallableConversation>.Failure(CallError.Blocked, "Cuộc trò chuyện đang bị chặn.");
        if (conversation.Members.Count != 2 || conversation.Members.All(member => member.UserId != callerId))
        {
            return CallResult<CallableConversation>.Failure(CallError.Forbidden, "Bạn không phải thành viên cuộc trò chuyện.");
        }
        if (conversation.Members.Any(member => member.AccountStatus != AccountStatus.Active || member.DeletedAt.HasValue))
        {
            return CallResult<CallableConversation>.Failure(CallError.Forbidden, "Người dùng không khả dụng.");
        }
        var receiverId = conversation.Members.Single(member => member.UserId != callerId).UserId;
        var busy = await dbContext.CallSessions
            .AsNoTracking()
            .AnyAsync(call =>
                (call.CallerId == callerId || call.ReceiverId == callerId || call.CallerId == receiverId || call.ReceiverId == receiverId) &&
                (call.Status == CallStatus.Calling || call.Status == CallStatus.Accepted),
                cancellationToken);
        if (busy) return CallResult<CallableConversation>.Failure(CallError.Busy, "Người dùng đang bận.");
        return CallResult<CallableConversation>.Success(new(receiverId));
    }

    private async Task<CallResult<CallSessionResponse>> TransitionAsync(
        Guid callId,
        Guid? userId,
        IReadOnlyCollection<CallStatus> allowedStatuses,
        CallStatus nextStatus,
        bool requireCaller = false,
        bool requireReceiver = false,
        CancellationToken cancellationToken = default)
    {
        var current = await dbContext.CallSessions
            .AsNoTracking()
            .Where(call => call.Id == callId)
            .Select(call => new { call.CallerId, call.ReceiverId, call.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null) return CallResult<CallSessionResponse>.Failure(CallError.NotFound, "Không tìm thấy cuộc gọi.");
        if (userId.HasValue)
        {
            if (requireCaller && current.CallerId != userId.Value) return CallResult<CallSessionResponse>.Failure(CallError.Forbidden, "Chỉ người gọi mới được hủy.");
            if (requireReceiver && current.ReceiverId != userId.Value) return CallResult<CallSessionResponse>.Failure(CallError.Forbidden, "Chỉ người nhận mới được thao tác.");
            if (!requireCaller && !requireReceiver && current.CallerId != userId.Value && current.ReceiverId != userId.Value)
            {
                return CallResult<CallSessionResponse>.Failure(CallError.Forbidden, "Bạn không có quyền thao tác cuộc gọi này.");
            }
        }
        if (!allowedStatuses.Contains(current.Status))
        {
            return CallResult<CallSessionResponse>.Failure(CallError.InvalidState, "Trạng thái cuộc gọi không hợp lệ.");
        }

        var now = DateTime.UtcNow;
        var updated = await dbContext.CallSessions
            .Where(call => call.Id == callId && allowedStatuses.Contains(call.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(call => call.Status, nextStatus)
                .SetProperty(call => call.EndedAt, now)
                .SetProperty(call => call.Duration, 0)
                .SetProperty(call => call.UpdatedAt, now), cancellationToken);
        if (updated != 1)
        {
            return CallResult<CallSessionResponse>.Failure(CallError.InvalidState, "Cuộc gọi đã được xử lý trên thiết bị khác.");
        }

        return await GetByIdAsync(userId ?? current.CallerId, callId, cancellationToken);
    }

    private static bool IsTerminal(CallStatus status) =>
        status is CallStatus.Rejected or CallStatus.Missed or CallStatus.Cancelled or CallStatus.Ended;

    private static CallSessionResponse Map(CallSession call) => new(
        call.Id,
        call.ConversationId,
        new CallParticipantResponse(call.CallerId, call.Caller.DisplayName, call.Caller.AvatarUrl),
        new CallParticipantResponse(call.ReceiverId, call.Receiver.DisplayName, call.Receiver.AvatarUrl),
        call.CallType,
        call.Status,
        call.StartedAt,
        call.AnsweredAt,
        call.EndedAt,
        call.Duration,
        call.CreatedAt,
        call.UpdatedAt);

    private sealed record CallableConversation(Guid ReceiverId);
}
