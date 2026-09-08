using Viora.Application.Chat;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Chat;

public sealed class StickerMessageValidatorTests
{
    private readonly SendChatMessageValidator validator = new();

    [Fact]
    public async Task Sticker_message_requires_sticker_id()
    {
        var command = CreateStickerCommand(null);

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.StickerId));
    }

    [Fact]
    public async Task Sticker_message_rejects_content_and_attachments()
    {
        var command = CreateStickerCommand(Guid.NewGuid()) with
        {
            Content = "client-controlled-url",
            Attachments = [new("https://cdn.example/sticker.webp", null, "image/webp", null, 10, null)]
        };

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Content));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Attachments));
    }

    [Fact]
    public async Task Sticker_message_accepts_only_server_resolved_sticker_id()
    {
        var command = CreateStickerCommand(Guid.NewGuid());

        var result = await validator.ValidateAsync(command);

        Assert.True(result.IsValid);
    }

    private static SendChatMessageCommand CreateStickerCommand(Guid? stickerId) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            MessageType.Sticker,
            null,
            [],
            null,
            stickerId);
}
