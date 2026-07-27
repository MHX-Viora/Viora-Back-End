using Microsoft.Extensions.Logging.Abstractions;
using Viora.Application.Mentions;
using Viora.Application.Notifications;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class MentionServiceTests
{
    [Fact]
    public async Task Message_mentions_are_saved_without_creating_notifications()
    {
        var sender = new User { Id = Guid.NewGuid(), DisplayName = "Sender" };
        var mentionedUser = new User { Id = Guid.NewGuid(), DisplayName = "Member" };
        var repository = new MentionRepository(sender, mentionedUser);
        var notifications = new CaptureNotificationService();
        var service = new MentionService(
            repository,
            notifications,
            NullLogger<MentionService>.Instance);

        var result = await service.CreateAsync(
            sender.Id,
            Guid.NewGuid(),
            MentionTargetType.Message,
            [mentionedUser.Id],
            CancellationToken.None);

        Assert.Single(result);
        Assert.Single(repository.Mentions);
        Assert.Equal(MentionTargetType.Message, repository.Mentions[0].TargetType);
        Assert.Equal(0, notifications.SendCount);
    }

    private sealed class MentionRepository(User sender, User mentionedUser) : IMentionRepository
    {
        public IReadOnlyList<Mention> Mentions { get; private set; } = [];

        public Task<User?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(userId == sender.Id ? sender : null);

        public Task<IReadOnlyList<User>> GetEligibleUsersAsync(
            Guid mentionedByUserId,
            IReadOnlyList<Guid> userIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<User>>(
                userIds.Contains(mentionedUser.Id) ? [mentionedUser] : []);

        public Task AddRangeAsync(IReadOnlyList<Mention> mentions, CancellationToken cancellationToken)
        {
            Mentions = mentions;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<MentionNotificationReference?> GetNotificationReferenceAsync(
            Guid targetId,
            MentionTargetType targetType,
            CancellationToken cancellationToken) =>
            Task.FromResult<MentionNotificationReference?>(
                new(targetId, NotificationReferenceType.Conversation));

        public Task<IReadOnlyList<MentionSearchResponse>> SearchAsync(
            Guid currentUserId,
            string keyword,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MentionSearchResponse>>([]);
    }

    private sealed class CaptureNotificationService : INotificationService
    {
        public int SendCount { get; private set; }

        public Task<Notification> SendAsync(
            SendNotificationCommand command,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new Notification());
        }

        public Task PublishAsync(Notification notification, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
