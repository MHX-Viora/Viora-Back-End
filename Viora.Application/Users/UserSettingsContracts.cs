using FluentValidation;
using MediatR;

namespace Viora.Application.Users;

public enum UserSettingsError
{
    NotFound,
    Invalid
}

public sealed record UserSettingsResult<T>(bool IsSuccess, T? Value, UserSettingsError? Error, string? Message)
{
    public static UserSettingsResult<T> Success(T value) => new(true, value, null, null);
    public static UserSettingsResult<T> Failure(UserSettingsError error, string message) => new(false, default, error, message);
}

public sealed record GetUserSettingsQuery(Guid UserId) : IRequest<UserSettingsResult<UserSettingsResponse>>;

public sealed record UpdateUserSettingsCommand(
    Guid UserId,
    bool? IsPrivate,
    bool? AllowMessageEveryone,
    bool? AllowComment,
    bool? AllowMention,
    string? Language,
    string? Theme) : IRequest<UserSettingsResult<UserSettingsResponse>>;

public sealed record UserSettingsResponse(
    bool IsPrivate,
    bool AllowMessageEveryone,
    bool AllowComment,
    bool AllowMention,
    string Language,
    string Theme);

public interface IUserSettingsRepository
{
    Task<bool> ActiveUserExistsAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserSettingsResponse> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserSettingsResponse> UpdateAsync(UpdateUserSettingsCommand command, CancellationToken cancellationToken);
}

public sealed class GetUserSettingsValidator : AbstractValidator<GetUserSettingsQuery>
{
    public GetUserSettingsValidator() => RuleFor(x => x.UserId).NotEmpty();
}

public sealed class UpdateUserSettingsValidator : AbstractValidator<UpdateUserSettingsCommand>
{
    private static readonly string[] SupportedThemes = ["light", "dark", "system"];

    public UpdateUserSettingsValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Language)
            .Must(value => value is null || !string.IsNullOrWhiteSpace(value))
            .WithMessage("Language khong duoc rong.")
            .MaximumLength(20);
        RuleFor(x => x.Theme)
            .Must(value => value is null || SupportedThemes.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Theme khong hop le.")
            .MaximumLength(20);
    }
}
