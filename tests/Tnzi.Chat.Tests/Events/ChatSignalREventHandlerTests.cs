using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tnzi.Chat.Events;
using Tnzi.Chat.Events.Handlers;
using Tnzi.SignalR.Services;

namespace Tnzi.Chat.Tests.Events;

public class ChatSignalREventHandlerTests
{
    [Fact]
    public async Task NewMessage_Should_Push_To_Recipients()
    {
        var push = new Mock<IMessagePushService>();
        var handler = new ChatSignalREventHandler(NullLogger<ChatSignalREventHandler>.Instance, push.Object);
        var recipients = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        await handler.HandleAsync(new ConversationMessageSentEvent
        {
            ConversationId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            SenderId = Guid.NewGuid(),
            ContentType = MessageContentType.Text,
            Preview = "hi",
            RecipientUserIds = recipients
        });

        push.Verify(p => p.PushToUsersAsync(recipients, "Chat.NewMessage", It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public async Task Handler_Should_Swallow_Push_Exceptions()
    {
        var push = new Mock<IMessagePushService>();
        push.Setup(p => p.PushToUsersAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<string>(), It.IsAny<object[]>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var handler = new ChatSignalREventHandler(NullLogger<ChatSignalREventHandler>.Instance, push.Object);

        // 不抛出即通过
        await handler.HandleAsync(new ConversationMessageSentEvent
        {
            ConversationId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            RecipientUserIds = new List<Guid> { Guid.NewGuid() },
            Preview = "x"
        });

        // Verify the handler DID attempt the push (and swallowed the thrown exception)
        push.Verify(p => p.PushToUsersAsync(It.IsAny<IEnumerable<Guid>>(), "Chat.NewMessage", It.IsAny<object[]>()), Times.Once);
    }
}
