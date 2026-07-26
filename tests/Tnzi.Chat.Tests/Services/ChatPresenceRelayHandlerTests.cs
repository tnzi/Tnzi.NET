using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Moq;
using Tnzi.Chat.Events.Handlers;
using Tnzi.Domain.Repositories;
using Tnzi.Identity.Presence.Events;
using Tnzi.SignalR.Services;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 承接原 PresenceService.BroadcastAsync 的 chat 专属联系人扇出用例——presence 机制迁出后，
/// 这段逻辑成为订阅 UserPresenceChangedEvent 的 ChatPresenceRelayHandler。
/// </summary>
public class ChatPresenceRelayHandlerTests
{
    private static ChatPresenceRelayHandler Build(
        List<ConversationMember> members,
        List<Conversation> conversations,
        Mock<IMessagePushService>? push)
    {
        var memberRepo = new Mock<IRepository<ConversationMember, Guid>>();
        var convRepo = new Mock<IRepository<Conversation, Guid>>();

        memberRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<ConversationMember, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ConversationMember, bool>> p, CancellationToken _) =>
                members.Where(p.Compile()).ToList());
        convRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<Conversation, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<Conversation, bool>> p, CancellationToken _) =>
                conversations.Where(p.Compile()).ToList());

        return new ChatPresenceRelayHandler(
            new Mock<ILogger<ChatPresenceRelayHandler>>().Object,
            memberRepo.Object, convRepo.Object, push?.Object);
    }

    [Fact]
    public async Task Relay_Should_Push_PresenceChanged_To_Contacts_Sharing_NonSystem_Conversation()
    {
        var me = Guid.NewGuid();
        var contactA = Guid.NewGuid();
        var contactB = Guid.NewGuid();
        var groupConvId = Guid.NewGuid();

        var conversations = new List<Conversation>
        {
            new() { Id = groupConvId, Type = ConversationType.Group }
        };
        var members = new List<ConversationMember>
        {
            new() { ConversationId = groupConvId, UserId = me, RemovedAt = null },
            new() { ConversationId = groupConvId, UserId = contactA, RemovedAt = null },
            new() { ConversationId = groupConvId, UserId = contactB, RemovedAt = null },
        };

        var push = new Mock<IMessagePushService>();
        var handler = Build(members, conversations, push);

        await handler.HandleAsync(new UserPresenceChangedEvent { UserId = me, Status = UserPresenceStatus.Online });

        push.Verify(p => p.PushToUsersAsync(
            It.Is<IEnumerable<Guid>>(ids =>
                ids.OrderBy(x => x).SequenceEqual(new[] { contactA, contactB }.OrderBy(x => x))),
            ChatPresenceRelayHandler.PresenceChangedMethod,
            It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public async Task Relay_Should_Exclude_Contacts_Only_Sharing_System_Conversation()
    {
        var me = Guid.NewGuid();
        var systemPeer = Guid.NewGuid();   // shares ONLY a System conversation → must be excluded
        var systemConvId = Guid.NewGuid();

        var conversations = new List<Conversation>
        {
            new() { Id = systemConvId, Type = ConversationType.System }
        };
        var members = new List<ConversationMember>
        {
            new() { ConversationId = systemConvId, UserId = me, RemovedAt = null },
            new() { ConversationId = systemConvId, UserId = systemPeer, RemovedAt = null },
        };

        var push = new Mock<IMessagePushService>();
        var handler = Build(members, conversations, push);

        await handler.HandleAsync(new UserPresenceChangedEvent { UserId = me, Status = UserPresenceStatus.Online });

        // No non-System conversation in common → no push at all.
        push.Verify(p => p.PushToUsersAsync(
            It.IsAny<IEnumerable<Guid>>(), It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
    }

    [Fact]
    public async Task Relay_NoPush_Is_NoOp()
    {
        var me = Guid.NewGuid();
        // push == null → SignalR not loaded; handler must short-circuit without touching repos.
        var handler = Build(new List<ConversationMember>(), new List<Conversation>(), push: null);

        // Should not throw.
        await handler.HandleAsync(new UserPresenceChangedEvent { UserId = me, Status = UserPresenceStatus.Online });
    }
}
