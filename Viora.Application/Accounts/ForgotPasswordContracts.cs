using FluentValidation;
using MediatR;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Viora.Domain.Entities;

namespace Viora.Application.Accounts;

public sealed record GetForgotPasswordStatusQuery(string Identifier)
    : IRequest<ForgotPasswordResult<ForgotPasswordStatusResponse>>;

public sealed record SetForgotPasswordPhoneCommand(
    Guid UserId,
    string PhoneNumber,
    string FirebaseToken)
    : IRequest<ForgotPasswordResult<ForgotPasswordMessageResponse>>;

public sealed record ResetForgottenPasswordCommand(
    string FirebaseToken,
    string NewPassword,
    string Identifier)
    : IRequest<ForgotPasswordResult<ForgotPasswordMessageResponse>>;

public sealed record ForgotPasswordStatusResponse(
    Guid UserId,
    string? PhoneNumber,
    bool HasPhoneNumber);

public sealed record ForgotPasswordMessageResponse(bool Success, string Message);

public enum ForgotPasswordOutcome
{
    Success,
    NotFound,
    Unauthorized,
    Conflict,
    ValidationFailed,
    SamePassword
}

public sealed record ForgotPasswordResult<T>(
    ForgotPasswordOutcome Outcome,
    T? Value,
    string Message,
    string? Code = null)
{
    public static ForgotPasswordResult<T> Success(T value, string message = "") =>
        new(ForgotPasswordOutcome.Success, value, message);

    public static ForgotPasswordResult<T> Failure(
        ForgotPasswordOutcome outcome,
        string message,
        string? code = null) =>
        new(outcome, default, message, code);
}

public interface IForgotPasswordRepository
{
    Task<Account?> FindByIdentifierAsync(
        string? email,
        IReadOnlyList<string> phoneCandidates,
        CancellationToken cancellationToken);

    Task<Account?> FindByPhoneAsync(
        IReadOnlyList<string> phoneCandidates,
        CancellationToken cancellationToken);

    Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken);
    Task<Account?> GetAsync(Guid accountId, CancellationToken cancellationToken);
    Task<bool> PhoneExistsAsync(string phoneNumber, Guid excludingAccountId, CancellationToken cancellationToken);
    Task SavePhoneAsync(Account account, string phoneNumber, CancellationToken cancellationToken);
    Task ChangePasswordAndRevokeRefreshTokensAsync(
        Account account,
        string passwordHash,
        DateTime changedAt,
        CancellationToken cancellationToken);
}

public sealed record FirebaseVerifiedIdentity(string? Email, string? PhoneNumber);

public interface IFirebaseIdentityTokenVerifier
{
    Task<FirebaseVerifiedIdentity?> VerifyAsync(
        string firebaseToken,
        CancellationToken cancellationToken);
}

public interface IPasswordResetHasher
{
    string Hash(string password);
}

public static class ForgotPasswordIdentifier
{
    public static (string? Email, IReadOnlyList<string> PhoneCandidates) Parse(string identifier)
    {
        var value = identifier.Trim();
        if (MailAddress.TryCreate(value, out var address) &&
            string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase))
        {
            return (value.ToLowerInvariant(), []);
        }

        var normalized = NormalizePhone(value);
        return (null, PhoneCandidates(normalized));
    }

    public static string NormalizePhone(string phoneNumber)
    {
        var value = Regex.Replace(phoneNumber.Trim(), "[\\s()\\-]", string.Empty);
        if (Regex.IsMatch(value, "^0[0-9]{8,10}$"))
        {
            value = $"+84{value[1..]}";
        }

        if (!Regex.IsMatch(value, "^\\+[1-9][0-9]{7,14}$"))
        {
            throw new AccountValidationException(
                "INVALID_PHONE_NUMBER",
                "Số điện thoại không hợp lệ.");
        }

        return value;
    }

    public static IReadOnlyList<string> PhoneCandidates(string normalizedPhone)
    {
        if (normalizedPhone.StartsWith("+84", StringComparison.Ordinal) &&
            normalizedPhone.Length > 3)
        {
            return [normalizedPhone, $"0{normalizedPhone[3..]}"];
        }

        return [normalizedPhone];
    }
}

public sealed class GetForgotPasswordStatusValidator : AbstractValidator<GetForgotPasswordStatusQuery>
{
    public GetForgotPasswordStatusValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Thông tin đăng nhập không được để trống.")
            .MaximumLength(255);
    }
}

public sealed class SetForgotPasswordPhoneValidator : AbstractValidator<SetForgotPasswordPhoneCommand>
{
    public SetForgotPasswordPhoneValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.FirebaseToken).NotEmpty().MaximumLength(4096);
    }
}

public sealed class ResetForgottenPasswordValidator : AbstractValidator<ResetForgottenPasswordCommand>
{
    public ResetForgottenPasswordValidator()
    {
        RuleFor(x => x.FirebaseToken).NotEmpty().MaximumLength(4096);
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(255);
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Mật khẩu mới không được để trống.")
            .Length(8, 100).WithMessage("Mật khẩu mới phải từ 8-100 ký tự.")
            .Matches("[A-Z]").WithMessage("Mật khẩu mới phải chứa ít nhất 1 chữ hoa.")
            .Matches("[a-z]").WithMessage("Mật khẩu mới phải chứa ít nhất 1 chữ thường.")
            .Matches("[0-9]").WithMessage("Mật khẩu mới phải chứa ít nhất 1 số.");
    }
}
