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
            return CallResult<CallSessionResponse>.Failure(validation.Error ?? CallError.Validation, validation.Message ?? "Khong the goi.");
        }

        var now = DateTime.UtcNow;
        var call = new CallSession
        {
            ConversationId = command.ConversationId,
            CallerId = command.CallerId,
            ReceiverId = validation.Value.ReceiverId,
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

    public Task<CallResult<CallSessionResponse>> AcceptCallAsync(AcceptCallCommand command, CancellationToken cancellationToken) =>
        TransitionAsync(command.CallId, command.UserId, [CallStatus.Calling], CallStatus.Accepted, requireReceiver: true, cancellationToken: cancellationToken);

    public Task<CallResult<CallSessionResponse>> RejectCallAsync(RejectCallCommand command, CancellationToken cancellationToken) =>
        TransitionAsync(command.CallId, command.UserId, [CallStatus.Calling], CallStatus.Rejected, requireReceiver: true, cancellationToken: cancellationToken);

    public Task<CallResult<CallSessionResponse>> CancelCallAsync(CancelCallCommand command, CancellationToken cancellationToken) =>
        TransitionAsync(command.CallId, command.UserId, [CallStatus.Calling], CallStatus.Cancelled, requireCaller: true, cancellationToken: cancellationToken);

    public Task<CallResult<CallSessionResponse>> EndCallAsync(EndCallCommand command, CancellationToken cancellationToken) =>
        TransitionAsync(command.CallId, command.UserId, [CallStatus.Accepted], CallStatus.Ended, cancellationToken: cancellationToken);

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
            return CallResult<CallSessionResponse>.Failure(CallError.NotFound, "Khong tim thay cuoc goi.");
        }
        if (call.CallerId != userId && call.ReceiverId != userId)
        {
            return CallResult<CallSessionResponse>.Failure(CallError.Forbidden, "Ban khong co quyen xem cuoc goi nay.");
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
        if (conversation is null) return CallResult<CallableConversation>.Failure(CallError.ConversationNotFound, "Khong tim thay cuoc tro chuyen.");
        if (conversation.DeletedAt.HasValue) return CallResult<CallableConversation>.Failure(CallError.ConversationDissolved, "Conversation has been dissolved.");
        if (conversation.ConversationType != ConversationType.Private) return CallResult<CallableConversation>.Failure(CallError.Validation, "Chi ho tro goi 1-1 trong phong chat rieng.");
        if (conversation.IsBlocked) return CallResult<CallableConversation>.Failure(CallError.Blocked, "Cuoc tro chuyen dang bi chan.");
        if (conversation.Members.Count != 2 || conversation.Members.All(member => member.UserId != callerId))
        {
            return CallResult<CallableConversation>.Failure(CallError.Forbidden, "Ban khong phai thanh vien cuoc tro chuyen.");
        }
        if (conversation.Members.Any(member => member.AccountStatus != AccountStatus.Active || member.DeletedAt.HasValue))
        {
            return CallResult<CallableConversation>.Failure(CallError.Forbidden, "Nguoi dung khong kha dung.");
        }
        return CallResult<CallableConversation>.Success(new(conversation.Members.Single(member => member.UserId != callerId).UserId));
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
        var call = await dbContext.CallSessions
            .Include(value => value.Caller)
            .Include(value => value.Receiver)
            .FirstOrDefaultAsync(value => value.Id == callId, cancellationToken);
        if (call is null) return CallResult<CallSessionResponse>.Failure(CallError.NotFound, "Khong tim thay cuoc goi.");
        if (userId.HasValue)
        {
            if (requireCaller && call.CallerId != userId.Value) return CallResult<CallSessionResponse>.Failure(CallError.Forbidden, "Chi nguoi goi moi duoc huy.");
            if (requireReceiver && call.ReceiverId != userId.Value) return CallResult<CallSessionResponse>.Failure(CallError.Forbidden, "Chi nguoi nhan moi duoc thao tac.");
            if (!requireCaller && !requireReceiver && call.CallerId != userId.Value && call.ReceiverId != userId.Value)
            {
                return CallResult<CallSessionResponse>.Failure(CallError.Forbidden, "Ban khong co quyen thao tac cuoc goi nay.");
            }
        }
        if (!allowedStatuses.Contains(call.Status))
        {
            return CallResult<CallSessionResponse>.Failure(CallError.InvalidState, "Trang thai cuoc goi khong hop le.");
        }

        var now = DateTime.UtcNow;
        call.Status = nextStatus;
        if (nextStatus == CallStatus.Accepted) call.AnsweredAt = now;
        if (nextStatus is CallStatus.Rejected or CallStatus.Cancelled or CallStatus.Missed or CallStatus.Ended)
        {
            call.EndedAt = now;
            call.Duration = call.AnsweredAt.HasValue ? Math.Max(0, (int)(now - call.AnsweredAt.Value).TotalSeconds) : 0;
        }
        call.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return CallResult<CallSessionResponse>.Success(Map(call));
    }

    private static CallSessionResponse Map(CallSession call) => new(
        call.Id,
        call.ConversationId,
        new CallParticipantResponse(call.CallerId, call.Caller.DisplayName, call.Caller.AvatarUrl),
        new CallParticipantResponse(call.ReceiverId, call.Receiver.DisplayName, call.Receiver.AvatarUrl),
        call.Status,
        call.StartedAt,
        call.AnsweredAt,
        call.EndedAt,
        call.Duration,
        call.CreatedAt,
        call.UpdatedAt);

    private sealed record CallableConversation(Guid ReceiverId);
}
