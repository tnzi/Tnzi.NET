using System.Net.WebSockets;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// A7 hardening tests: anonymous connection cap, authed chatId binding,
/// RequireAuthentication rejection of anonymous clients, peer-scoped session.* methods,
/// and the inbound frame-reassembly cap.
/// </summary>
public class GatewayWebSocketHardeningTests
{
    private static GatewayWebSocketHandler CreateHandler(
        IPresenceTracker presence,
        int maxConnectionsPerUser = 2,
        bool requireAuthentication = false,
        IGateway? gateway = null,
        int maxInboundMessageBytes = 256 * 1024)
    {
        var gatewayMock = gateway ?? new Mock<IGateway>().Object;
        return new GatewayWebSocketHandler(
            gatewayMock,
            presence,
            NullLogger<GatewayWebSocketHandler>.Instance,
            maxConnectionsPerUser: maxConnectionsPerUser,
            heartbeatIntervalSeconds: 3600,
            requireAuthentication: requireAuthentication,
            maxInboundMessageBytes: maxInboundMessageBytes);
    }

    private static IPresenceTracker CreatePresenceWith(int anonymousCount)
    {
        var presence = new DefaultPresenceTracker(NullLogger<DefaultPresenceTracker>.Instance);
        for (var i = 0; i < anonymousCount; i++)
        {
            presence.TrackConnection(new GatewayConnectionInfo
            {
                ConnectionId = Guid.NewGuid().ToString("N"),
                UserId = null, // anonymous bucket
                ClientType = "websocket",
                ConnectedAt = DateTimeOffset.UtcNow
            });
        }
        return presence;
    }

    [Fact]
    public async Task HandleAsync_AnonymousOverCap_ClosesConnection()
    {
        // Arrange - already at the cap (2) of anonymous connections
        var presence = CreatePresenceWith(anonymousCount: 2);
        var handler = CreateHandler(presence, maxConnectionsPerUser: 2);
        var ws = new FakeWebSocket();

        // Act - the N+1 anonymous connection
        await handler.HandleAsync(ws, userId: null, CancellationToken.None);

        // Assert - connection rejected/closed, never tracked
        ws.CloseStatusValue.ShouldBe(WebSocketCloseStatus.PolicyViolation);
        presence.GetConnections().Count.ShouldBe(2);
    }

    [Fact]
    public async Task HandleAsync_AnonymousUnderCap_Accepts()
    {
        // Arrange - one anonymous connection exists, cap is 2
        var presence = CreatePresenceWith(anonymousCount: 1);
        var handler = CreateHandler(presence, maxConnectionsPerUser: 2);
        var ws = new FakeWebSocket();
        ws.EnqueueClose(); // immediately close so HandleAsync returns

        // Act
        await handler.HandleAsync(ws, userId: null, CancellationToken.None);

        // Assert - it was tracked (then removed on disconnect); not a policy-violation close
        ws.CloseStatusValue.ShouldNotBe(WebSocketCloseStatus.PolicyViolation);
    }

    [Fact]
    public async Task HandleAsync_RequireAuthenticationTrue_RejectsAnonymous()
    {
        // Arrange
        var presence = CreatePresenceWith(anonymousCount: 0);
        var handler = CreateHandler(presence, requireAuthentication: true);
        var ws = new FakeWebSocket();

        // Act
        await handler.HandleAsync(ws, userId: null, CancellationToken.None);

        // Assert - rejected before any tracking
        ws.CloseStatusValue.ShouldBe(WebSocketCloseStatus.PolicyViolation);
        presence.GetConnections().Count.ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // peer 身份解析：匿名命名空间与用户命名空间不可能相交
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolveChatId_AuthedUser_IgnoresClientSuppliedChatId()
    {
        // Arrange - authed user supplies someone else's chatId in the payload
        var payload = JsonSerializer.SerializeToElement(new { text = "hi", chatId = "victim-peer" });

        // Act
        var chatId = GatewayWebSocketHandler.ResolveChatId(payload, userId: "real-user", connectionId: "c1");

        // Assert - chatId is FORCED to the authed user identity, not the spoofed value
        chatId.ShouldBe("real-user");
    }

    [Fact]
    public void ResolveChatId_Anonymous_KeepsClientChatIdInsideTheAnonymousNamespace()
    {
        // 匿名客户端仍可自选话题标识（重连后接回自己的话题），但整体收进匿名命名空间。
        var payload = JsonSerializer.SerializeToElement(new { text = "hi", chatId = "session-abc" });

        var chatId = GatewayWebSocketHandler.ResolveChatId(payload, userId: null, connectionId: "c1");

        chatId.ShouldBe("anon:session-abc");
    }

    [Fact]
    public void ResolveChatId_AnonymousSpoofingAUserId_CannotReachThatUsersPeerNamespace()
    {
        // ★ 这一条是整个修复的要点：匿名客户端把受害者的 userId 填进 chatId。
        // 命名空间隔离之前，DefaultGateway.ResolveThreadIdAsync 会拿这个 chatId 去
        // threadStore 查出受害者的线程 —— 历史流回攻击者，之后的发言记进对方的线程。
        var payload = JsonSerializer.SerializeToElement(new { text = "hi", chatId = "victim-user" });

        var anonymous = GatewayWebSocketHandler.ResolveChatId(payload, userId: null, connectionId: "c1");
        var victim = GatewayWebSocketHandler.ResolveChatId(payload, userId: "victim-user", connectionId: "c2");

        anonymous.ShouldNotBe(victim);
        anonymous.ShouldStartWith(GatewayWebSocketHandler.AnonymousPeerPrefix);
        victim.ShouldBe("victim-user");
    }

    [Fact]
    public void ResolveChatId_AnonymousNoChatId_FallsBackToTheConnectionId()
    {
        // 缺省不再回退到共享的 "unknown" 桶：那让每个不带 chatId 的匿名客户端
        // 落在同一个 peer 上，彼此看得见对方的线程。
        var payload = JsonSerializer.SerializeToElement(new { text = "hi" });
        GatewayWebSocketHandler.ResolveChatId(payload, userId: null, connectionId: "conn-1")
            .ShouldBe("anon:conn-1");
    }

    [Fact]
    public void ResolvePeerId_AnonymousCallers_AreNamespacedPerConnection()
    {
        GatewayWebSocketHandler.ResolvePeerId(userId: null, connectionId: "c1").ShouldBe("anon:c1");
        GatewayWebSocketHandler.ResolvePeerId(userId: null, connectionId: "c2").ShouldBe("anon:c2");
        GatewayWebSocketHandler.ResolvePeerId(userId: "u1", connectionId: "c1").ShouldBe("u1");
    }

    // -------------------------------------------------------------------------
    // session.* 只作用于调用者自己的会话
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SessionList_Anonymous_AsksOnlyForItsOwnPeerSessions()
    {
        // ★ 此前 session.list 直调 GetSessionsAsync()，返回全部活跃会话（每条都带 PeerId =
        // 用户 id）—— 一个匿名客户端由此枚举出全部在线用户，再去接管他们的线程。
        var gatewayMock = new Mock<IGateway>();
        gatewayMock
            .Setup(g => g.GetPeerSessionsAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync([]);

        var handler = CreateHandler(CreatePresenceWith(0), gateway: gatewayMock.Object);
        var ws = new FakeWebSocket();
        ws.EnqueueText(SessionListFrame);
        ws.EnqueueClose();

        await handler.HandleAsync(ws, userId: null, CancellationToken.None);

        // 不带归属过滤的重载一次都不能被走到
        gatewayMock.Verify(g => g.GetSessionsAsync(It.IsAny<string?>()), Times.Never);
        gatewayMock.Verify(
            g => g.GetPeerSessionsAsync(It.Is<string>(pid => pid.StartsWith("anon:", StringComparison.Ordinal)), null),
            Times.Once);
    }

    [Fact]
    public async Task SessionList_AuthedUser_AsksForItsOwnUserIdOnly()
    {
        var gatewayMock = new Mock<IGateway>();
        gatewayMock
            .Setup(g => g.GetPeerSessionsAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync([]);

        var handler = CreateHandler(CreatePresenceWith(0), gateway: gatewayMock.Object);
        var ws = new FakeWebSocket();
        ws.EnqueueText(SessionListFrame);
        ws.EnqueueClose();

        await handler.HandleAsync(ws, userId: "real-user", CancellationToken.None);

        gatewayMock.Verify(g => g.GetSessionsAsync(It.IsAny<string?>()), Times.Never);
        gatewayMock.Verify(g => g.GetPeerSessionsAsync("real-user", null), Times.Once);
    }

    [Fact]
    public async Task SessionPrune_GoesThroughThePeerScopedOverload()
    {
        // ★ PruneSessionAsync(sessionKey) 落到 ConcurrentDictionary.TryRemove(key)：不看值，
        // 于是任何客户端都能删掉任意一条会话键。
        var gatewayMock = new Mock<IGateway>();
        gatewayMock
            .Setup(g => g.PrunePeerSessionAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var handler = CreateHandler(CreatePresenceWith(0), gateway: gatewayMock.Object);
        var ws = new FakeWebSocket();
        ws.EnqueueText(SessionFrame("session.prune"));
        ws.EnqueueClose();

        await handler.HandleAsync(ws, userId: null, CancellationToken.None);

        gatewayMock.Verify(g => g.PruneSessionAsync(It.IsAny<string>()), Times.Never);
        gatewayMock.Verify(
            g => g.PrunePeerSessionAsync(
                It.Is<string>(pid => pid.StartsWith("anon:", StringComparison.Ordinal)),
                VictimSessionKey),
            Times.Once);
    }

    [Fact]
    public async Task SessionGet_GoesThroughThePeerScopedOverload()
    {
        // 会话键是可推导的（agent:{id}:peer:{userId}），所以按键直取等于按用户 id 直取。
        var gatewayMock = new Mock<IGateway>();
        gatewayMock
            .Setup(g => g.GetPeerSessionAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((GatewaySession?)null);

        var handler = CreateHandler(CreatePresenceWith(0), gateway: gatewayMock.Object);
        var ws = new FakeWebSocket();
        ws.EnqueueText(SessionFrame("session.get"));
        ws.EnqueueClose();

        await handler.HandleAsync(ws, userId: null, CancellationToken.None);

        gatewayMock.Verify(g => g.GetSessionAsync(It.IsAny<string>()), Times.Never);
        gatewayMock.Verify(
            g => g.GetPeerSessionAsync(
                It.Is<string>(pid => pid.StartsWith("anon:", StringComparison.Ordinal)),
                VictimSessionKey),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // 分片帧重组上限
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_OversizedFragmentedMessage_ClosesWithMessageTooBig()
    {
        // 没有上限时，一条连接一直发 EndOfMessage=false 的续帧就能把服务端内存吃光。
        var handler = CreateHandler(CreatePresenceWith(0), maxConnectionsPerUser: 5, maxInboundMessageBytes: 32);

        var ws = new FakeWebSocket();
        ws.EnqueueTextFragment(new string('x', 20));
        ws.EnqueueTextFragment(new string('y', 20));
        ws.EnqueueClose();

        await handler.HandleAsync(ws, userId: null, CancellationToken.None);

        ws.CloseStatusValue.ShouldBe(WebSocketCloseStatus.MessageTooBig);
    }

    [Fact]
    public async Task HandleAsync_FragmentedMessageUnderTheCap_IsReassembledAndDispatched()
    {
        // 对照组：上限不能把正常的分片消息也拒掉。
        GatewayRequest? captured = null;
        var gatewayMock = new Mock<IGateway>();
        gatewayMock
            .Setup(g => g.ProcessStreamingAsync(It.IsAny<GatewayRequest>(), It.IsAny<CancellationToken>()))
            .Returns((GatewayRequest r, CancellationToken _) =>
            {
                captured = r;
                return AsyncEmpty();
            });

        var handler = CreateHandler(
            CreatePresenceWith(0), maxConnectionsPerUser: 5,
            gateway: gatewayMock.Object, maxInboundMessageBytes: 1024);

        var ws = new FakeWebSocket();
        ws.EnqueueTextFragment(ChatSendFrameHead);
        ws.EnqueueText(ChatSendFrameTail);
        ws.EnqueueClose();

        await handler.HandleAsync(ws, userId: "real-user", CancellationToken.None);

        captured.ShouldNotBeNull();
        captured.UserMessage.ShouldBe("hello");
        ws.CloseStatusValue.ShouldNotBe(WebSocketCloseStatus.MessageTooBig);
    }

    [Fact]
    public async Task HandleChatSend_AuthedUser_SessionKeyBoundToUserNotSpoofedChatId()
    {
        // Arrange - authed user "real-user" sends a chat.send carrying "victim-peer" as chatId.
        // The gateway must receive ChatId == userId so the session key cannot impersonate another peer.
        GatewayRequest? captured = null;
        var gatewayMock = new Mock<IGateway>();
        gatewayMock
            .Setup(g => g.ProcessStreamingAsync(It.IsAny<GatewayRequest>(), It.IsAny<CancellationToken>()))
            .Returns((GatewayRequest r, CancellationToken _) =>
            {
                captured = r;
                return AsyncEmpty();
            });

        var presence = CreatePresenceWith(anonymousCount: 0);
        var handler = CreateHandler(presence, gateway: gatewayMock.Object);

        var ws = new FakeWebSocket();
        ws.EnqueueText(ChatSendFrameHead + " \"payload\": { \"text\": \"hello\", \"channel\": \"web\", \"chatId\": \"victim-peer\" } }");
        ws.EnqueueClose();

        // Act
        await handler.HandleAsync(ws, userId: "real-user", CancellationToken.None);

        // Assert
        captured.ShouldNotBeNull();
        captured.ChatId.ShouldBe("real-user");
        captured.UserId.ShouldBe("real-user");
    }

    private const string VictimSessionKey = "agent:abc:peer:victim-user";

    private const string SessionListFrame =
        "{ \"type\": \"request\", \"id\": \"r1\", \"method\": \"session.list\", \"payload\": {} }";

    private const string ChatSendFrameHead =
        "{ \"type\": \"request\", \"id\": \"r1\", \"method\": \"chat.send\",";

    private const string ChatSendFrameTail =
        " \"payload\": { \"text\": \"hello\", \"channel\": \"web\" } }";

    private static string SessionFrame(string method) =>
        $"{{ \"type\": \"request\", \"id\": \"r1\", \"method\": \"{method}\", \"payload\": {{ \"sessionKey\": \"{VictimSessionKey}\" }} }}";

    private static async IAsyncEnumerable<GatewayStreamChunk> AsyncEmpty()
    {
        await Task.CompletedTask;
        yield break;
    }
}
