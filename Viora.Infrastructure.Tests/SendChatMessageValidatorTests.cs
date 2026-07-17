using Viora.Application.Chat;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class SendChatMessageValidatorTests
{
    private readonly SendChatMessageValidator validator = new();

    [Fact]
    public void Text_message_requires_content_and_no_attachments()
    {
        var result = validator.Validate(new SendChatMessageCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            MessageType.Text,
            null,
            [new SendChatMessageAttachmentRequest("https://cdn.example/a.jpg", null, null, null, null)]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Content");
        Assert.Contains(result.Errors, error => error.PropertyName == "Attachments");
    }

    [Fact]
    public void Audio_message_requires_attachment_duration()
    {
        var result = validator.Validate(new SendChatMessageCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            MessageType.Audio,
            null,
            [new SendChatMessageAttachmentRequest("https://cdn.example/a.mp3", null, "audio/mpeg", 100, null)]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Attachments");
    }

    [Fact]
    public void Recall_message_cannot_be_sent_by_client()
    {
        var result = validator.Validate(new SendChatMessageCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            MessageType.Recall,
            "recall",
            null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "MessageType");
    }

    [Fact]
    public void Location_message_requires_valid_json_coordinates()
    {
        var result = validator.Validate(new SendChatMessageCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            MessageType.Location,
            """{"latitude":10.123,"longitude":106.123,"address":"Ho Chi Minh"}""",
            null));

        Assert.True(result.IsValid);
    }
}
