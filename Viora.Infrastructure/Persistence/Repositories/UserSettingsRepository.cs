using Microsoft.EntityFrameworkCore;
using Viora.Application.Users;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class UserSettingsRepository(AppDbContext db) : IUserSettingsRepository
{
    public Task<bool> ActiveUserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId &&
            user.Account.Status == AccountStatus.Active &&
            user.Account.DeletedAt == null,
            cancellationToken);

    public async Task<UserSettingsResponse> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var settings = await db.UserSettings.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (settings is null)
        {
            settings = Default(userId);
            db.UserSettings.Add(settings);
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToResponse(settings);
    }

    public async Task<UserSettingsResponse> UpdateAsync(UpdateUserSettingsCommand command, CancellationToken cancellationToken)
    {
        var settings = await db.UserSettings.SingleOrDefaultAsync(x => x.UserId == command.UserId, cancellationToken);
        if (settings is null)
        {
            settings = Default(command.UserId);
            db.UserSettings.Add(settings);
        }

        if (command.IsPrivate.HasValue) settings.IsPrivate = command.IsPrivate.Value;
        if (command.AllowMessageEveryone.HasValue) settings.AllowMessageEveryone = command.AllowMessageEveryone.Value;
        if (command.AllowComment.HasValue) settings.AllowComment = command.AllowComment.Value;
        if (command.AllowMention.HasValue) settings.AllowMention = command.AllowMention.Value;
        if (command.Language is not null) settings.Language = command.Language.Trim();
        if (command.Theme is not null) settings.Theme = command.Theme.Trim().ToLowerInvariant();

        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(settings);
    }

    private static UserSettings Default(Guid userId) => new()
    {
        UserId = userId,
        IsPrivate = false,
        AllowMessageEveryone = true,
        AllowComment = true,
        AllowMention = true,
        Language = "vi",
        Theme = "light"
    };

    private static UserSettingsResponse ToResponse(UserSettings settings) =>
        new(
            settings.IsPrivate,
            settings.AllowMessageEveryone,
            settings.AllowComment,
            settings.AllowMention,
            settings.Language,
            settings.Theme);
}
