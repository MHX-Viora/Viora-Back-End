using FluentValidation;
using MediatR;

namespace Viora.Application.Users;

public sealed class GetUserSettingsHandler(
    IUserSettingsRepository repository,
    IValidator<GetUserSettingsQuery> validator)
    : IRequestHandler<GetUserSettingsQuery, UserSettingsResult<UserSettingsResponse>>
{
    public async Task<UserSettingsResult<UserSettingsResponse>> Handle(GetUserSettingsQuery request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Invalid<UserSettingsResponse>(validation);
        if (!await repository.ActiveUserExistsAsync(request.UserId, cancellationToken))
        {
            return UserSettingsResult<UserSettingsResponse>.Failure(UserSettingsError.NotFound, "Khong tim thay nguoi dung.");
        }

        return UserSettingsResult<UserSettingsResponse>.Success(await repository.GetOrCreateAsync(request.UserId, cancellationToken));
    }

    internal static UserSettingsResult<T> Invalid<T>(FluentValidation.Results.ValidationResult validation) =>
        UserSettingsResult<T>.Failure(
            UserSettingsError.Invalid,
            validation.Errors.FirstOrDefault()?.ErrorMessage ?? "Du lieu khong hop le.");
}

public sealed class UpdateUserSettingsHandler(
    IUserSettingsRepository repository,
    IValidator<UpdateUserSettingsCommand> validator)
    : IRequestHandler<UpdateUserSettingsCommand, UserSettingsResult<UserSettingsResponse>>
{
    public async Task<UserSettingsResult<UserSettingsResponse>> Handle(UpdateUserSettingsCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return GetUserSettingsHandler.Invalid<UserSettingsResponse>(validation);
        if (!await repository.ActiveUserExistsAsync(request.UserId, cancellationToken))
        {
            return UserSettingsResult<UserSettingsResponse>.Failure(UserSettingsError.NotFound, "Khong tim thay nguoi dung.");
        }

        return UserSettingsResult<UserSettingsResponse>.Success(await repository.UpdateAsync(request, cancellationToken));
    }
}
