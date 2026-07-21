using FluentValidation;
using Viora.Application.Realtime;

namespace Viora.Application.Chat;

public sealed class JoinGroupHandler(
    IJoinGroupRepository repository,
    IRealtimeService realtime,
    IValidator<JoinGroupCommand> validator)
    : MediatR.IRequestHandler<JoinGroupCommand, GroupChatResult<JoinGroupResponse>>
{
    public async Task<GroupChatResult<JoinGroupResponse>> Handle(JoinGroupCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return GroupChatResult<JoinGroupResponse>.Failure(
                GroupChatError.Validation,
                validation.Errors.FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.");
        }

        var result = await repository.JoinByInviteCodeAsync(request with { InviteCode = request.InviteCode.Trim() }, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return GroupChatResult<JoinGroupResponse>.Failure(result.Error ?? GroupChatError.Validation, result.Message ?? "Không thể tham gia nhóm.");
        }

        var groupName = result.Value.Response.ConversationId.ToString();
        await realtime.AddUsersToGroupAsync([request.CurrentUserId], groupName, cancellationToken);
        await realtime.SendToUsersAsync(result.Value.Recipients, RealtimeEvents.ReceiveMessage, result.Value.SystemMessage, cancellationToken);
        await realtime.SendToUsersAsync(
            result.Value.Recipients,
            RealtimeEvents.MemberAdded,
            result.Value.MemberPayload,
            cancellationToken);

        return GroupChatResult<JoinGroupResponse>.Success(result.Value.Response);
    }
}
