using Tnzi.AI.Channels.Entities;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;
using Tnzi.AI.Channels.Options;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// 网关上的运行以谁的名义跑：已认证的 WebSocket 连接 = 那个框架用户，其余（匿名连接、IM 渠道）= 没有用户。
/// </summary>
/// <remarks>
/// <c>QuotaMiddleware</c> 只看 <see cref="AgentRunRequest.UserId"/>：为空时 USD 预算门与按用户 token 配额都跳过，
/// 绑定到用户的工具权限规则也匹配不上。网关此前从不填它，于是一个已登录用户在聊天端点上额度用完之后，
/// 换到 <c>/ws/gateway</c> 照样一直聊下去，用量也不计入他的配额。
/// IM 渠道的发送者 id 是平台账号（Slack <c>U…</c>、Telegram 数字），不是框架用户，绝不能拿来当配额主体。
/// </remarks>
public class GatewayRunUserTests
{
    private static readonly Guid DefaultAgent = Guid.Parse("22222222-0000-0000-0000-000000000002");

    private const string ChatSendFrame =
        "{ \"type\": \"request\", \"id\": \"r1\", \"method\": \"chat.send\", \"payload\": { \"text\": \"hello\" } }";

    [Fact]
    public async Task WebSocket_AuthenticatedConnection_RunsAsThatUser()
    {
        var userId = Guid.Parse("33333333-0000-0000-0000-000000000003");
        var (gateway, runs) = CreateGateway();
        var handler = CreateHandler(gateway);
        var ws = new FakeWebSocket();
        ws.EnqueueText(ChatSendFrame);
        ws.EnqueueClose();

        await handler.HandleAsync(ws, userId.ToString(), CancellationToken.None);

        runs.Count.ShouldBe(1);
        runs[0].UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task WebSocket_AnonymousConnection_RunsWithoutAUser()
    {
        var (gateway, runs) = CreateGateway();
        var handler = CreateHandler(gateway);
        var ws = new FakeWebSocket();
        ws.EnqueueText(ChatSendFrame);
        ws.EnqueueClose();

        await handler.HandleAsync(ws, userId: null, CancellationToken.None);

        runs.Count.ShouldBe(1);
        runs[0].UserId.ShouldBeNull();
    }

    [Fact]
    public async Task ImChannelPeer_IsNotTakenForAFrameworkUser_EvenWhenItLooksLikeAGuid()
    {
        // 平台账号 id 恰好是 GUID 形状也不行：配额主体只来自服务端认证过的身份
        var (gateway, runs) = CreateGateway();

        await gateway.ProcessAsync(new GatewayRequest
        {
            Channel = "slack",
            ChatId = "C1",
            UserId = Guid.NewGuid().ToString(),
            UserMessage = "hello"
        });

        runs.Count.ShouldBe(1);
        runs[0].UserId.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessAsync_AuthenticatedUser_RunsAsThatUser()
    {
        var userId = Guid.NewGuid();
        var (gateway, runs) = CreateGateway();

        await gateway.ProcessAsync(new GatewayRequest
        {
            Channel = GatewayWebSocketHandler.WebSocketChannel,
            ChatId = userId.ToString(),
            UserId = userId.ToString(),
            AuthenticatedUserId = userId,
            UserMessage = "hello"
        });

        runs.Count.ShouldBe(1);
        runs[0].UserId.ShouldBe(userId);
    }

    private static (DefaultGateway Gateway, List<AgentRunRequest> Runs) CreateGateway()
    {
        var options = new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions { DefaultAgentId = DefaultAgent });
        var binder = new DefaultSessionBinder(Array.Empty<SessionBindingRule>(), options);

        var runs = new List<AgentRunRequest>();
        var runtime = new Mock<IAgentRuntime>();
        runtime
            .Setup(r => r.RunStreamingAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns((AgentRunRequest r, CancellationToken _) =>
            {
                runs.Add(r);
                return NoChunks();
            });
        runtime
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunRequest r, CancellationToken _) =>
            {
                runs.Add(r);
                return new AgentRunResult { Response = "ok" };
            });

        var services = new Mock<IServiceProvider>();
        services.Setup(sp => sp.GetService(typeof(IAgentRuntime))).Returns(runtime.Object);
        services.Setup(sp => sp.GetService(typeof(IChannelThreadStore))).Returns(new Mock<IChannelThreadStore>().Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(services.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return (new DefaultGateway(binder, scopeFactory.Object, options, NullLogger<DefaultGateway>.Instance), runs);
    }

    private static GatewayWebSocketHandler CreateHandler(IGateway gateway)
    {
        var presence = new Mock<IPresenceTracker>();
        presence.Setup(p => p.GetConnections()).Returns(Array.Empty<GatewayConnectionInfo>());

        return new GatewayWebSocketHandler(
            gateway,
            presence.Object,
            NullLogger<GatewayWebSocketHandler>.Instance,
            maxConnectionsPerUser: 5,
            heartbeatIntervalSeconds: 3600,
            requireAuthentication: false,
            maxInboundMessageBytes: 256 * 1024);
    }

    private static async IAsyncEnumerable<AgentStreamChunk> NoChunks()
    {
        await Task.CompletedTask;
        yield break;
    }
}
