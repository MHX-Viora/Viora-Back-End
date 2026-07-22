using MediatR;

namespace Viora.Application.Accounts;

public sealed record ChangePasswordRequestCommand(
    Guid AccountId,
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword) : IRequest<ChangePasswordResult>;

public sealed class ChangePasswordHandler(IAccountService accountService)
    : IRequestHandler<ChangePasswordRequestCommand, ChangePasswordResult>
{
    public Task<ChangePasswordResult> Handle(
        ChangePasswordRequestCommand request,
        CancellationToken cancellationToken) =>
        accountService.ChangePasswordAsync(
            new ChangePasswordCommand(
                request.AccountId,
                request.CurrentPassword,
                request.NewPassword,
                request.ConfirmPassword),
            cancellationToken);
}
