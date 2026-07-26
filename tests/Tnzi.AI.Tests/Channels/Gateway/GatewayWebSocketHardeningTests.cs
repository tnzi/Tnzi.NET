using System.Net.WebSockets;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// A7 hardening tests: anonymous connection cap, authed chatId binding,
/// and RequireAuthentication rejection of anonymous clients.
/// </summary>
public class GatewayWebSocketHardeningTests
{
    private static GatewayWebSocketHandler CreateHandler(
        IPresenceTracker presence,
        int maxConnectionsPerUser = 2,
        bool requireAuthentication = false,
        IGateway? gateway = null)
    {
        var gatewayMock = gateway ?? new Mock<IGateway>().Object;
        return new GatewayWebSocketHandler(
            gatewayMock,
            presence,
            NullLogger<GatewayWebSocketHandler>.Instance,
            maxConnectionsPerUser: maxConnectionsPerUser,
            heartbeatIntervalSeconds: 3600,
            requireAuthentication: requireAuthentication);
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

    [Fact]
    public void ResolveChatId_AuthedUser_IgnoresClientSuppliedChatId()
    {
        // Arrange - authed user supplies someone else's chatId in the payload
        var payload = JsonSerializer.SerializeToElement(new { text = "hi", chatId = "victim-peer" });

        // Act
        var chatId = GatewayWebSocketHandler.ResolveChatId(payload, userId: "real-user");

        // Assert - chatId is FORCED to the authed user identity, not the spoofed value
        chatId.ShouldBe("real-user");
    }

    [Fact]
    public void ResolveChatId_Anonymous_UsesClientSuppliedChatId()
    {
        // Arrange - anonymous client may pick its own chatId discriminator
        var payload = JsonSerializer.SerializeToElement(new { text = "hi", chatId = "session-abc" });

        // Act
        var chatId = GatewayWebSocketHandler.ResolveChatId(payload, userId: null);

        // Assert
        chatId.ShouldBe("session-abc");
    }

    [Fact]
    public void ResolveChatId_AnonymousNoChatId_FallsBackToUnknown()
    {
        var payload = JsonSerializer.SerializeToElement(new { text = "hi" });
        GatewayWebSocketHandler.ResolveChatId(payload, userId: null).ShouldBe("unknown");
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
        ws.EnqueueText("""
        { "type": "request", "id": "r1", "method": "chat.send",
          "payload": { "text": "hello", "channel": "web", "chatId": "victim-peer" } }
        """);
        ws.EnqueueClose();

        // Act
        await handler.HandleAsync(ws, userId: "real-user", CancellationToken.None);

        // Assert
        captured.ShouldNotBeNull();
        captured.ChatId.ShouldBe("real-user");
        captured.UserId.ShouldBe("real-user");
    }

    private static async IAsyncEnumerable<GatewayStreamChunk> AsyncEmpty()
    {
        await Task.CompletedTask;
        yield break;
    }
}
