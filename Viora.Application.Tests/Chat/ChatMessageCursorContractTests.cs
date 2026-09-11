using Viora.Application.Chat;

namespace Viora.Application.Tests.Chat;

public sealed class ChatMessageCursorContractTests
{
    [Fact]
    public void Message_query_exposes_additive_before_and_after_cursors()
    {
        var queryType = typeof(GetChatConversationMessagesQuery);

        Assert.Equal(typeof(Guid?), queryType.GetProperty("AfterMessageId")?.PropertyType);
        Assert.Equal(typeof(Guid?), queryType.GetProperty("BeforeMessageId")?.PropertyType);
    }
}
