using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;
using Tnzi.AI.Channels.Options;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// 匿名 WebSocket 客户端在活跃会话列表里各是各的，并且列得出自己的会话。
/// </summary>
/// <remarks>
/// 此前匿名连接一律以字面量 <c>"anonymous"</c> 作为发送者：默认作用域下所有匿名客户端落在同一把会话键上、
/// 互相覆盖会话记录；自选 chatId 的匿名客户端，会话记在 <c>anon:{chatId}</c> 名下，而它查询时用的是
/// <c>anon:{连接 id}</c>，<c>session.list</c> 永远为空。整条链路用真实的 <see cref="DefaultGateway"/> 跑。
/// </remarks>
public sealed class GatewayAnonymousSessionTests
{
    private static readonly Guid AgentId = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a5");

    [Fact]
    public async Task TwoAnonymousClients_GetTwoSessions_NotOneSharedRecord()
    {
        var gateway = CreateGateway();

        await RunConnectionAsync(gateway, ChatSend());
        await RunConnectionAsync(gateway, ChatSend());

        var sessions = await gateway.GetSessionsAsync();
        sessions.Count.ShouldBe(2, "each anonymous connection is its own peer");
        sessions.Select(s => s.SessionKey).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task AnonymousClientWithItsOwnChatId_SeesItsSessionInSessionList()
    {
        var gateway = CreateGateway();

        var sent = await RunConnectionAsync(gateway, ChatSend(chatId: "my-topic"), SessionList);

        var listed = ReadSessionList(sent);
        var own = listed.ShouldHaveSingleItem();
        own.GetProperty("peerId").GetString().ShouldBe(GatewayWebSocketHandler.AnonymousPeerPrefix + "my-topic");
    }

    [Fact]
    public async Task AnonymousClient_DoesNotSeeAnotherAnonymousClientsSession()
    {
        var gateway = CreateGateway();
        await RunConnectionAsync(gateway, ChatSend(chatId: "victim-topic"));

        var sent = await RunConnectionAsync(gateway, SessionList);

        ReadSessionList(sent).ShouldBeEmpty();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private const string SessionList =
        "{ \"type\": \"request\", \"id\": \"list\", \"method\": \"session.list\", \"payload\": {} }";

    private static string ChatSend(string? chatId = null)
        => chatId is null
            ? "{ \"type\": \"request\", \"id\": \"send\", \"method\": \"chat.send\", \"payload\": { \"text\": \"hello\" } }"
            : $"{{ \"type\": \"request\", \"id\": \"send\", \"method\": \"chat.send\", \"payload\": {{ \"text\": \"hello\", \"chatId\": \"{chatId}\" }} }}";

    /// <summary>一条匿名连接：依次发完这些帧再关闭，返回服务端发出的全部帧。</summary>
    private static async Task<List<string>> RunConnectionAsync(IGateway gateway, params string[] frames)
    {
        var handler = new GatewayWebSocketHandler(
            gateway,
            new DefaultPresenceTracker(NullLogger<DefaultPresenceTracker>.Instance),
            NullLogger<GatewayWebSocketHandler>.Instance,
            maxConnectionsPerUser: 10,
            heartbeatIntervalSeconds: 3600);

        var ws = new FakeWebSocket();
        foreach (var frame in frames)
        {
            ws.EnqueueText(frame);
        }

        ws.EnqueueClose();
        await handler.HandleAsync(ws, userId: null, CancellationToken.None);
        return ws.SentMessages;
    }

    private static List<JsonElement> ReadSessionList(IEnumerable<string> sent)
    {
        var response = sent
            .Select(m => JsonDocument.Parse(m).RootElement)
            .Single(e => e.TryGetProperty("method", out var m) && m.GetString() == "session.list");
        return [.. response.GetProperty("payload").EnumerateArray().Select(e => e.Clone())];
    }

    private static DefaultGateway CreateGateway()
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime
            .Setup(r => r.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns((AgentRunRequest request, CancellationToken _) => Reply(request));

        var store = new Mock<IChannelThreadStore>();
        store.Setup(s => s.GetThreadIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((Guid?)null);

        var services = new ServiceCollection();
        services.AddSingleton(runtime.Object);
        services.AddSingleton(store.Object);
        var provider = services.BuildServiceProvider();

        var options = new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions
        {
            DefaultAgentId = AgentId,
            DefaultScope = SessionScope.PerPeer,
            StreamingThrottleMs = 0
        });
        return new DefaultGateway(new DefaultSessionBinder([], options),
            provider.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<DefaultGateway>.Instance);
    }

    private static async IAsyncEnumerable<AgentStreamChunk> Reply(AgentRunRequest request)
    {
        await Task.Yield();
        request.ThreadId ??= Guid.NewGuid();
        yield return new AgentStreamChunk { Text = "hi", FinishReason = "stop" };
    }
}
