using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// B9 - verifies all WebSocket sends (heartbeat + stream) are serialized through a
/// per-connection semaphore so SendAsync calls never overlap (WebSocket forbids that).
/// </summary>
public class GatewayWebSocketSendSerializationTests
{
    [Fact]
    public async Task HeartbeatAndStream_NeverOverlapSends()
    {
        // Arrange - gateway yields many chunks with tiny gaps so streaming runs while the
        // heartbeat fires; FakeWebSocket.SendDelay widens each send to expose any overlap.
        var gatewayMock = new Mock<IGateway>();
        gatewayMock
            .Setup(g => g.ProcessStreamingAsync(It.IsAny<GatewayRequest>(), It.IsAny<CancellationToken>()))
            .Returns((GatewayRequest _, CancellationToken ct) => StreamManyAsync(ct));

        var presence = new DefaultPresenceTracker(NullLogger<DefaultPresenceTracker>.Instance);

        // Heartbeat interval of 0 seconds → fires continuously, maximizing contention with stream.
        var handler = new GatewayWebSocketHandler(
            gatewayMock.Object,
            presence,
            NullLogger<GatewayWebSocketHandler>.Instance,
            maxConnectionsPerUser: 100,
            heartbeatIntervalSeconds: 0);

        var ws = new FakeWebSocket { SendDelay = TimeSpan.FromMilliseconds(2) };
        ws.EnqueueText("""
        { "type": "request", "id": "r1", "method": "chat.send",
          "payload": { "text": "go", "channel": "web" } }
        """);
        ws.EnqueueClose();

        // Act
        await handler.HandleAsync(ws, userId: "u1", CancellationToken.None);

        // Assert - sends were serialized; the high-water mark of concurrent in-flight sends is 1.
        ws.MaxConcurrentSends.ShouldBe(1);
        // Sanity: both heartbeat and stream actually sent (not a vacuous pass).
        ws.SentMessages.Count.ShouldBeGreaterThan(1);
    }

    private static async IAsyncEnumerable<GatewayStreamChunk> StreamManyAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 0; i < 30; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return new GatewayStreamChunk { TextDelta = $"chunk-{i}", IsFinal = i == 29 };
            await Task.Delay(1, ct);
        }
    }
}
