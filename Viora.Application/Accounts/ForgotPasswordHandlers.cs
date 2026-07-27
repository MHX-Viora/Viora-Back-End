using FluentValidation;
using MediatR;

namespace Viora.Application.Accounts;

public sealed class GetForgotPasswordStatusHandler(
    IForgotPasswordRepository repository,
    IValidator<GetForgotPasswordStatusQuery> validator)
    : IRequestHandler<GetForgotPasswordStatusQuery, ForgotPasswordResult<ForgotPasswordStatusResponse>>
{
    public async Task<ForgotPasswordResult<ForgotPasswordStatusResponse>> Handle(
        GetForgotPasswordStatusQuery request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ForgotPasswordResult<ForgotPasswordStatusResponse>.Failure(
                ForgotPasswordOutcome.ValidationFailed,
                validation.Errors[0].ErrorMessage);
        }

        try
        {
            var identifier = ForgotPasswordIdentifier.Parse(request.Identifier);
            var account = await repository.FindByIdentifierAsync(
                identifier.Email,
                identifier.PhoneCandidates,
                cancellationToken);
            if (account is null)
            {
                return ForgotPasswordResult<ForgotPasswordStatusResponse>.Failure(
                    ForgotPasswordOutcome.NotFound,
                    "Không tìm thấy tài khoản.");
            }

            return ForgotPasswordResult<ForgotPasswordStatusResponse>.Success(
                new(account.Id, account.Phone, !string.IsNullOrWhiteSpace(account.Phone)));
        }
        catch (AccountValidationException exception)
        {
            return ForgotPasswordResult<ForgotPasswordStatusResponse>.Failure(
                ForgotPasswordOutcome.ValidationFailed,
                exception.Message,
                exception.Code);
        }
    }
}

public sealed class SetForgotPasswordPhoneHandler(
    IForgotPasswordRepository repository,
    IFirebaseIdentityTokenVerifier firebaseTokenVerifier,
    IValidator<SetForgotPasswordPhoneCommand> validator)
    : IRequestHandler<SetForgotPasswordPhoneCommand, ForgotPasswordResult<ForgotPasswordMessageResponse>>
{
    public async Task<ForgotPasswordResult<ForgotPasswordMessageResponse>> Handle(
        SetForgotPasswordPhoneCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Invalid(validation.Errors[0].ErrorMessage);
        }

        string phoneNumber;
        try
        {
            phoneNumber = ForgotPasswordIdentifier.NormalizePhone(request.PhoneNumber);
        }
        catch (AccountValidationException exception)
        {
            return Invalid(exception.Message, exception.Code);
        }

        var verifiedIdentity = await firebaseTokenVerifier.VerifyAsync(
            request.FirebaseToken,
            cancellationToken);
        var verifiedPhone = verifiedIdentity?.PhoneNumber;
        if (verifiedPhone is null ||
            !string.Equals(
                ForgotPasswordIdentifier.NormalizePhone(verifiedPhone),
                phoneNumber,
                StringComparison.Ordinal))
        {
            return ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
                ForgotPasswordOutcome.Unauthorized,
                "Firebase token không hợp lệ hoặc không khớp số điện thoại.");
        }

        var account = await repository.GetAsync(request.UserId, cancellationToken);
        if (account is null)
        {
            return ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
                ForgotPasswordOutcome.NotFound,
                "Không tìm thấy tài khoản.");
        }

        if (!string.IsNullOrWhiteSpace(account.Phone))
        {
            return ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
                ForgotPasswordOutcome.Conflict,
                "Tài khoản đã có số điện thoại.",
                "PHONE_ALREADY_SET");
        }

        if (await repository.PhoneExistsAsync(phoneNumber, account.Id, cancellationToken))
        {
            return ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
                ForgotPasswordOutcome.Conflict,
                "Số điện thoại đã được tài khoản khác sử dụng.",
                "PHONE_EXISTS");
        }

        await repository.SavePhoneAsync(account, phoneNumber, cancellationToken);
        return ForgotPasswordResult<ForgotPasswordMessageResponse>.Success(
            new(true, "Cập nhật số điện thoại thành công."));
    }

    private static ForgotPasswordResult<ForgotPasswordMessageResponse> Invalid(
        string message,
        string? code = null) =>
        ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
            ForgotPasswordOutcome.ValidationFailed,
            message,
            code);
}

public sealed class ResetForgottenPasswordHandler(
    IForgotPasswordRepository repository,
    IFirebaseIdentityTokenVerifier firebaseTokenVerifier,
    IPasswordHasher passwordVerifier,
    IPasswordResetHasher passwordHasher,
    IValidator<ResetForgottenPasswordCommand> validator)
    : IRequestHandler<ResetForgottenPasswordCommand, ForgotPasswordResult<ForgotPasswordMessageResponse>>
{
    public async Task<ForgotPasswordResult<ForgotPasswordMessageResponse>> Handle(
        ResetForgottenPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
                ForgotPasswordOutcome.ValidationFailed,
                validation.Errors[0].ErrorMessage);
        }

        var verifiedIdentity = await firebaseTokenVerifier.VerifyAsync(
            request.FirebaseToken,
            cancellationToken);
        if (verifiedIdentity is null)
        {
            return ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
                ForgotPasswordOutcome.Unauthorized,
                "Firebase token không hợp lệ.");
        }

        Viora.Domain.Entities.Account? account;
        try
        {
            var identifier = ForgotPasswordIdentifier.Parse(request.Identifier);
            if (identifier.Email is not null)
            {
                if (!string.Equals(
                    verifiedIdentity.Email,
                    identifier.Email,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
                        ForgotPasswordOutcome.Unauthorized,
                        "Firebase token không khớp thông tin đăng nhập.");
                }

                account = await repository.FindByEmailAsync(
                    identifier.Email,
                    cancellationToken);
            }
            else
            {
                var verifiedPhone = verifiedIdentity.PhoneNumber is null
                    ? null
                    : ForgotPasswordIdentifier.NormalizePhone(verifiedIdentity.PhoneNumber);
                if (verifiedPhone is null ||
                    !identifier.PhoneCandidates.Contains(verifiedPhone))
                {
                    return ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
                        ForgotPasswordOutcome.Unauthorized,
                        "Firebase token không khớp thông tin đăng nhập.");
                }

                account = await repository.FindByPhoneAsync(
                    identifier.PhoneCandidates,
                    cancellationToken);
            }
        }
        catch (AccountValidationException exception)
        {
            return ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
                ForgotPasswordOutcome.ValidationFailed,
                exception.Message,
                exception.Code);
        }
        if (account is null)
        {
            return ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
                ForgotPasswordOutcome.NotFound,
                "Không tìm thấy tài khoản.");
        }

        if (passwordVerifier.Verify(request.NewPassword, account.PasswordHash))
        {
            return ForgotPasswordResult<ForgotPasswordMessageResponse>.Failure(
                ForgotPasswordOutcome.SamePassword,
                "Mật khẩu mới không được trùng mật khẩu cũ.");
        }

        await repository.ChangePasswordAndRevokeRefreshTokensAsync(
            account,
            passwordHasher.Hash(request.NewPassword),
            DateTime.UtcNow,
            cancellationToken);

        return ForgotPasswordResult<ForgotPasswordMessageResponse>.Success(
            new(true, "Đổi mật khẩu thành công."),
            "Đổi mật khẩu thành công.");
    }
}
