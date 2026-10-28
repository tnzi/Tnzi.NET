using Tnzi.AI.Channels.Controllers.Admin;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// 管理端 Gateway 诊断端点的租户隔离：在线连接与活跃会话都放在进程内单例里，
/// 不经过任何仓储，因此全局租户过滤器管不到它们，必须自己按租户筛。
/// </summary>
public class GatewayAdminTenantIsolationTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid AgentId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    [Fact]
    public void Connections_TenantAdmin_SeesOnlyItsOwnTenantsConnections()
    {
        var presence = PresenceWithOneConnectionPerTenant();
        var controller = CreateController(presence, gateway: null, multiTenancy: true, currentTenantId: TenantA);

        var connections = controller.GetConnections().Data.ShouldNotBeNull();

        connections.Select(c => c.UserId).ShouldBe(["user-a"]);
    }

    [Fact]
    public void Connections_HostContext_SeesOnlyHostConnections()
    {
        // 与全局租户过滤器同一口径：严格等值，没有「宿主看全部」的特例
        var presence = PresenceWithOneConnectionPerTenant();
        var controller = CreateController(presence, gateway: null, multiTenancy: true, currentTenantId: null);

        var connections = controller.GetConnections().Data.ShouldNotBeNull();

        connections.Select(c => c.UserId).ShouldBe(["host-user"]);
    }

    [Fact]
    public void Connections_MultiTenancyDisabled_ReturnsEverything()
    {
        var presence = PresenceWithOneConnectionPerTenant();
        var controller = CreateController(presence, gateway: null, multiTenancy: false, currentTenantId: TenantA);

        controller.GetConnections().Data.ShouldNotBeNull().Count.ShouldBe(3);
    }

    [Fact]
    public void Connections_MultiTenancyEnabled_WithoutACurrentTenantService_ReturnsNothing()
    {
        // 多租户开启却解析不到当前租户：不知道调用者是谁，就一条都不给（失败关闭）
        var controller = new DefaultGatewayAdminController(
            new Mock<IGateway>().Object,
            PresenceWithOneConnectionPerTenant(),
            bindingRuleRepository: null,
            currentTenant: null,
            multiTenancyOptions: MsOptions.Create(new MultiTenancyOptions { Enabled = true }));

        controller.GetConnections().Data.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public async Task Status_TenantAdmin_CountsOnlyItsOwnTenantsConnectionsAndSessions()
    {
        var presence = PresenceWithOneConnectionPerTenant();
        var gateway = CreateGateway();
        await gateway.ProcessAsync(Request("chat-a", TenantA));
        await gateway.ProcessAsync(Request("chat-b", TenantB));
        await gateway.ProcessAsync(Request("chat-b2", TenantB));

        var controller = CreateController(presence, gateway, multiTenancy: true, currentTenantId: TenantA);

        var status = (await controller.GetStatus()).Data.ShouldNotBeNull();

        status.ConnectedWebSocketCount.ShouldBe(1);
        status.ActiveSessionCount.ShouldBe(1);
    }

    [Fact]
    public async Task Gateway_RecordsTheRequestTenantOnTheSession()
    {
        var gateway = CreateGateway();
        await gateway.ProcessAsync(Request("chat-a", TenantA));

        var session = (await gateway.GetSessionsAsync()).ShouldHaveSingleItem();

        session.TenantId.ShouldBe(TenantA);
    }

    [Fact]
    public async Task WebSocketHandler_RecordsTheConnectionTenant()
    {
        var presence = new DefaultPresenceTracker(NullLogger<DefaultPresenceTracker>.Instance);
        GatewayConnectionInfo? tracked = null;
        presence.OnPresenceChanged += e =>
        {
            if (e.IsOnline)
            {
                tracked = presence.GetConnections().Single(c => c.ConnectionId == e.ConnectionId);
            }
        };

        var handler = new GatewayWebSocketHandler(
            new Mock<IGateway>().Object, presence, NullLogger<GatewayWebSocketHandler>.Instance,
            heartbeatIntervalSeconds: 3600);
        var ws = new FakeWebSocket();
        ws.EnqueueClose();

        await handler.HandleAsync(ws, "user-a", CancellationToken.None, tenantId: TenantA);

        tracked.ShouldNotBeNull().TenantId.ShouldBe(TenantA);
    }

    [Fact]
    public void TenantId_IsNotPartOfTheWireShape()
    {
        // 租户只用于服务端筛选，不改变管理端与 WS 客户端看到的 JSON 形状
        var connectionJson = JsonSerializer.Serialize(new GatewayConnectionInfo { ConnectionId = "c", TenantId = TenantA });
        var sessionJson = JsonSerializer.Serialize(new GatewaySession { SessionKey = "k", TenantId = TenantA });

        connectionJson.ShouldNotContain(TenantA.ToString());
        sessionJson.ShouldNotContain(TenantA.ToString());
    }

    private static DefaultPresenceTracker PresenceWithOneConnectionPerTenant()
    {
        var presence = new DefaultPresenceTracker(NullLogger<DefaultPresenceTracker>.Instance);
        presence.TrackConnection(Connection("user-a", TenantA));
        presence.TrackConnection(Connection("user-b", TenantB));
        presence.TrackConnection(Connection("host-user", null));
        return presence;
    }

    private static GatewayConnectionInfo Connection(string userId, Guid? tenantId) => new()
    {
        ConnectionId = Guid.NewGuid().ToString("N"),
        UserId = userId,
        ClientType = "websocket",
        ConnectedAt = DateTimeOffset.UtcNow,
        TenantId = tenantId
    };

    private static GatewayRequest Request(string chatId, Guid tenantId) => new()
    {
        Channel = "telegram",
        ChatId = chatId,
        UserId = chatId,
        UserMessage = "hi",
        TenantId = tenantId
    };

    private static DefaultGatewayAdminController CreateController(
        IPresenceTracker presence, IGateway? gateway, bool multiTenancy, Guid? currentTenantId)
    {
        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(t => t.Id).Returns(currentTenantId);

        return new DefaultGatewayAdminController(
            gateway ?? new Mock<IGateway>().Object,
            presence,
            bindingRuleRepository: null,
            currentTenant: currentTenant.Object,
            multiTenancyOptions: MsOptions.Create(new MultiTenancyOptions { Enabled = multiTenancy }));
    }

    private static DefaultGateway CreateGateway()
    {
        var options = new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions { DefaultAgentId = AgentId });
        var binder = new DefaultSessionBinder([], options);

        var runtime = new Mock<IAgentRuntime>();
        runtime
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "ok", ThreadId = Guid.NewGuid() });

        var services = new Mock<IServiceProvider>();
        services.Setup(sp => sp.GetService(typeof(IAgentRuntime))).Returns(runtime.Object);
        services.Setup(sp => sp.GetService(typeof(IChannelThreadStore))).Returns(new Mock<IChannelThreadStore>().Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(services.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new DefaultGateway(binder, scopeFactory.Object, options, NullLogger<DefaultGateway>.Instance);
    }
}
