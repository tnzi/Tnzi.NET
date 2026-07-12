using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using Moq;
using Tnzi.Chat.Options;
using Tnzi.Chat.Services;
using Tnzi.Domain.Repositories;
using Tnzi.Results;
using Tnzi.SignalR.Services;

namespace Tnzi.Chat.Tests.Services;

public class PresenceServiceTests
{
    private static IOptionsSnapshot<ChatOptions> Options(bool allowInvisible = true)
    {
        var opt = new Mock<IOptionsSnapshot<ChatOptions>>();
        opt.Setup(o => o.Value).Returns(new ChatOptions { AllowInvisible = allowInvisible });
        return opt.Object;
    }

    private static PresenceService Build(
        Mock<IConnectionManager>? conn = null,
        List<UserPresence>? presences = null,
        bool allowInvisible = true)
    {
        var sp = new Mock<IServiceProvider>();
        var presenceRepo = new Mock<IRepository<UserPresence, Guid>>();
        var memberRepo = new Mock<IRepository<ConversationMember, Guid>>();
        var convRepo = new Mock<IRepository<Conversation, Guid>>();
        presenceRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<UserPresence, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<UserPresence, bool>> p, CancellationToken _) =>
                (presences ?? new()).Where(p.Compile()).ToList());
        return new PresenceService(sp.Object, presenceRepo.Object, memberRepo.Object, convRepo.Object,
            Options(allowInvisible), connectionManager: conn?.Object, push: null);
    }

    /// <summary>
    /// Builds a PresenceService wired with in-memory member/conversation data so BroadcastAsync's
    /// contact fan-out can be exercised. ConnectionManager is null → manual-only effective status.
    /// </summary>
    private static PresenceService BuildForBroadcast(
        List<ConversationMember> members,
        List<Conversation> conversations,
        Mock<IMessagePushService>? push,
        List<UserPresence>? presences = null)
    {
        var sp = new Mock<IServiceProvider>();
        var presenceRepo = new Mock<IRepository<UserPresence, Guid>>();
        var memberRepo = new Mock<IRepository<ConversationMember, Guid>>();
        var convRepo = new Mock<IRepository<Conversation, Guid>>();

        presenceRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<UserPresence, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<UserPresence, bool>> p, CancellationToken _) =>
                (presences ?? new()).Where(p.Compile()).ToList());
        memberRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<ConversationMember, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ConversationMember, bool>> p, CancellationToken _) =>
                members.Where(p.Compile()).ToList());
        convRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<Conversation, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<Conversation, bool>> p, CancellationToken _) =>
                conversations.Where(p.Compile()).ToList());

        return new PresenceService(sp.Object, presenceRepo.Object, memberRepo.Object, convRepo.Object,
            Options(), connectionManager: null, push: push?.Object);
    }

    [Fact]
    public async Task ResolveEffective_NoRecord_ConnectedManager_Online()
    {
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        var svc = Build(conn);
        var uid = Guid.NewGuid();
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Online);
    }

    [Fact]
    public async Task ResolveEffective_OnlineIntent_NotConnected_Offline()
    {
        var uid = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(uid)).ReturnsAsync(false);
        var svc = Build(conn, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Online, LastSeenAt = DateTime.UtcNow } });
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Offline);
        r.Single().LastSeenAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ResolveEffective_Invisible_ShownOffline_EvenIfConnected()
    {
        var uid = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(uid)).ReturnsAsync(true);
        var svc = Build(conn, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Invisible } });
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Offline);
    }

    [Fact]
    public async Task SetStatus_Invisible_Should_Fail_403_When_Invisible_Disabled()
    {
        // AllowInvisible=false → 服务端强制拒绝隐身意图（前端已隐藏选项，此为兜底）。
        // 该拒绝在读取 CurrentUser 之前短路，故无需完整鉴权上下文。
        var svc = Build(allowInvisible: false);
        var r = await svc.SetStatusAsync(UserPresenceStatus.Invisible);
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);
    }

    [Fact]
    public async Task ResolveEffective_Invisible_Disabled_Resolves_As_Online_When_Connected()
    {
        // 禁用隐身后，历史隐身意图不再对外隐藏——有连接时按在线解析。
        var uid = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(uid)).ReturnsAsync(true);
        var svc = Build(conn, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Invisible } },
            allowInvisible: false);
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Online);
    }

    [Fact]
    public async Task ResolveEffective_Busy_Connected_Busy()
    {
        var uid = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(uid)).ReturnsAsync(true);
        var svc = Build(conn, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Busy } });
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Busy);
    }

    [Fact]
    public async Task ResolveEffective_NoConnectionManager_ManualOnlyMode()
    {
        var uid = Guid.NewGuid();
        var svc = Build(conn: null, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Away } });
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Away); // 无 SignalR → 手动值原样
    }

    // ── BroadcastAsync fan-out ──────────────────────────────────────────────────

    [Fact]
    public async Task Broadcast_Should_Push_PresenceChanged_To_Contacts_Sharing_NonSystem_Conversation()
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
        var svc = BuildForBroadcast(members, conversations, push);

        await svc.BroadcastAsync(me);

        push.Verify(p => p.PushToUsersAsync(
            It.Is<IEnumerable<Guid>>(ids =>
                ids.OrderBy(x => x).SequenceEqual(new[] { contactA, contactB }.OrderBy(x => x))),
            PresenceService.PresenceChangedMethod,
            It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public async Task Broadcast_Should_Exclude_Contacts_Only_Sharing_System_Conversation()
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
        var svc = BuildForBroadcast(members, conversations, push);

        await svc.BroadcastAsync(me);

        // No non-System conversation in common → no push at all.
        push.Verify(p => p.PushToUsersAsync(
            It.IsAny<IEnumerable<Guid>>(), It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
    }

    [Fact]
    public async Task Broadcast_NoPush_Is_NoOp()
    {
        var me = Guid.NewGuid();
        // push == null → SignalR not loaded; method must short-circuit without touching repos.
        var svc = BuildForBroadcast(new List<ConversationMember>(), new List<Conversation>(), push: null);

        // Should not throw.
        await svc.BroadcastAsync(me);
    }
}
