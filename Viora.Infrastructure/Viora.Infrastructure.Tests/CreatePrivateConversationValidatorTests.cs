using Viora.Application.Chat;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class CreatePrivateConversationValidatorTests
{
    private readonly CreatePrivateConversationValidator validator = new();

    [Fact]
    public async Task Rejects_creating_private_conversation_with_self()
    {
        var userId = Guid.NewGuid();

        var result = await validator.ValidateAsync(
            new CreatePrivateConversationCommand(userId, userId));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreatePrivateConversationCommand.UserId));
    }

    [Fact]
    public async Task Accepts_two_distinct_user_ids()
    {
        var result = await validator.ValidateAsync(
            new CreatePrivateConversationCommand(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
