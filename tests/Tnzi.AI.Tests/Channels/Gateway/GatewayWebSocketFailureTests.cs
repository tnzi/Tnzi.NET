using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// A failure while serving one request must answer that request and leave the connection open,
/// not escape the message loop and close the socket for every other request on it.
/// </summary>
public class GatewayWebSocketFailureTests
{
    [Fact]
    public async Task ChatSend_GatewayThrows_AnswersWithErrorAndKeepsServingTheConnection()
    {
        var gatewayMock = new Mock<IGateway>();
        gatewayMock
            .Setup(g => g.ProcessStreamingAsync(It.IsAny<GatewayRequest>(), It.IsAny<CancellationToken>()))
            .Returns((GatewayRequest _, CancellationToken _) => ThrowingStream());
        gatewayMock
            .Setup(g => g.GetPeerSessionsAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(Array.Empty<GatewaySession>());

        var handler = new GatewayWebSocketHandler(
            gatewayMock.Object,
            new DefaultPresenceTracker(NullLogger<DefaultPresenceTracker>.Instance),
            NullLogger<GatewayWebSocketHandler>.Instance,
            maxConnectionsPerUser: 100,
            heartbeatIntervalSeconds: 3600);

        var ws = new FakeWebSocket();
        ws.EnqueueText("""{ "type": "request", "id": "r1", "method": "chat.send", "payload": { "text": "go" } }""");
        ws.EnqueueText("""{ "type": "request", "id": "r2", "method": "session.list" }""");
        ws.EnqueueClose();

        await Should.NotThrowAsync(() => handler.HandleAsync(ws, userId: "u1", CancellationToken.None));

        var sent = ws.SentMessages.Select(m => JsonSerializer.Deserialize<GatewayMessage>(m)!).ToList();
        var failure = sent.Single(m => m.Id == "r1");
        failure.Type.ShouldBe("response");
        failure.Method.ShouldBe("chat.send");
        failure.Error.ShouldBe("error");

        // The next request on the same connection is still served.
        sent.ShouldContain(m => m.Id == "r2" && m.Method == "session.list" && m.Error == null);
    }

    private static async IAsyncEnumerable<GatewayStreamChunk> ThrowingStream()
    {
        await Task.Yield();
        throw new InvalidOperationException("database unavailable");
#pragma warning disable CS0162 // an iterator needs a yield to be one
        yield break;
#pragma warning restore CS0162
    }
}
