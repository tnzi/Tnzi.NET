using Microsoft.Data.Sqlite;
using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Entities;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;
using Tnzi.AI.Channels.Manager;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using Tnzi.AI.Tests.Channels.Gateway;
using Tnzi.EFCore.Data;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// P0 入站租户填充链端到端门禁 - 从 <see cref="ChannelManager"/> 的真实入站处理入口
/// （bus → ProcessMessageAsync → Gateway → SessionBinder）喂一条 <see cref="InboundMessage"/>，
/// 证明渠道 adapter options 配置的 TenantId 沿
/// <c>IChannelAdapter.TenantId → GatewayRequest.TenantId → SessionBindingContext.TenantId</c>
/// 完整传播，最终命中<b>正确租户</b>的数据库 <see cref="SessionBindingRule"/>，绝不命中他租户规则。
/// <para>
/// 关键约束：本测试<b>绝不</b>手工构造 <see cref="SessionBindingContext"/> 注入 TenantId——
/// 那是旧测试（SessionBinderMultiTenantTests 只测匹配算法）的盲区：算法正确但上游
/// GatewayRequest 没有 TenantId 字段时，带租户的规则在 IM 入站流量下永不命中。
/// </para>
/// </summary>
public sealed class ChannelManagerTenantFlowTests : IDisposable
{
    private static readonly Guid AgentA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AgentB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid DefaultAgent = Guid.Parse("dddddddd-0000-0000-0000-000000000003");

    private readonly SqliteConnection _connection;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public ChannelManagerTenantFlowTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // 种子：两条互斥的租户规则（同 channel/peer，仅 TenantId 不同）。
        // 无租户上下文写入 → 审计填充不会改写显式赋的 TenantId。
        using var seed = CreateDbContext(dataFilterManager: null);
        seed.Database.EnsureCreated();
        seed.Set<SessionBindingRule>().AddRange(
            new SessionBindingRule
            {
                Channel = "telegram", PeerId = "u1", AgentId = AgentA,
                Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = true, TenantId = _tenantA
            },
            new SessionBindingRule
            {
                Channel = "telegram", PeerId = "u1", AgentId = AgentB,
                Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = true, TenantId = _tenantB
            });
        seed.SaveChangesAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }

    // =====================================================================
    // 核心门禁 - 渠道归属租户 A 的入站消息命中租户 A 的规则，绝不命中租户 B
    // =====================================================================

    [Fact]
    public async Task InboundMessage_ChannelOwnedByTenantA_BindsTenantARule_NeverTenantB()
    {
        var harness = CreateHarness(channelTenantId: _tenantA);

        var captured = await RunInboundAsync(harness, new InboundMessage("telegram", "c1", "u1", "hello"));

        captured.AgentId.ShouldBe(AgentA,
            "Inbound traffic from a channel owned by Tenant A MUST bind to Tenant A's rule.");
        captured.AgentId.ShouldNotBe(AgentB);
    }

    [Fact]
    public async Task InboundMessage_ChannelOwnedByTenantB_BindsTenantBRule_NeverTenantA()
    {
        var harness = CreateHarness(channelTenantId: _tenantB);

        var captured = await RunInboundAsync(harness, new InboundMessage("telegram", "c1", "u1", "hello"));

        captured.AgentId.ShouldBe(AgentB,
            "Inbound traffic from a channel owned by Tenant B MUST bind to Tenant B's rule.");
        captured.AgentId.ShouldNotBe(AgentA);
    }

    [Fact]
    public async Task InboundMessage_ChannelWithoutTenant_MatchesNoTenantRule_FallsToDefault()
    {
        // 渠道未归属租户（单租户部署）→ 两条带租户的规则都不匹配 → 落到 Gateway 默认 Agent。
        // 这同时是"MT 关闭/租户为 null 时行为与现状完全一致"的回归证明。
        var harness = CreateHarness(channelTenantId: null);

        var captured = await RunInboundAsync(harness, new InboundMessage("telegram", "c1", "u1", "hello"));

        captured.AgentId.ShouldBe(DefaultAgent,
            "A channel without an owning tenant MUST NOT match tenant-scoped rules.");
        captured.AgentId.ShouldNotBe(AgentA);
        captured.AgentId.ShouldNotBe(AgentB);
    }

    [Fact]
    public async Task InboundMessage_ChannelOwnedByTenantA_EstablishesTenantContextInProcessingScopes()
    {
        // 处理作用域（ChannelManager + Gateway 内部作用域）必须切换到渠道归属租户，
        // 使 ChannelThreadMapping 等 IMultiTenant 实体的审计填充/全局过滤自然生效。
        var harness = CreateHarness(channelTenantId: _tenantA);

        await RunInboundAsync(harness, new InboundMessage("telegram", "c1", "u1", "hello"));

        harness.TenantRecorder.Changes.ShouldContain(_tenantA);
        harness.TenantRecorder.Changes.ShouldNotContain(_tenantB);
    }

    [Fact]
    public async Task InboundMessage_ChannelWithoutTenant_NeverTouchesTenantContext()
    {
        var harness = CreateHarness(channelTenantId: null);

        await RunInboundAsync(harness, new InboundMessage("telegram", "c1", "u1", "hello"));

        harness.TenantRecorder.Changes.ShouldBeEmpty(
            "Null channel tenant must leave the tenant context untouched (single-tenant behavior unchanged).");
    }

    // =====================================================================
    // 装配 - 真实 ChannelManager + 真实 Bus + 真实 DefaultGateway + 真实 DefaultSessionBinder
    //（DB 规则经真实仓储/过滤器管理器加载），仅 Runtime/ThreadStore/ThreadService 为测试替身
    // =====================================================================

    private sealed record Harness(
        ChannelManager Manager,
        InMemoryChannelMessageBus Bus,
        ConcurrentQueue<AgentRunRequest> CapturedRuns,
        RecordingCurrentTenant TenantRecorder);

    private Harness CreateHarness(Guid? channelTenantId)
    {
        var capturedRuns = new ConcurrentQueue<AgentRunRequest>();
        var tenantRecorder = new RecordingCurrentTenant();

        var runtimeMock = new Mock<IAgentRuntime>();
        runtimeMock
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunRequest, CancellationToken>((req, _) => capturedRuns.Enqueue(req))
            .ReturnsAsync(new AgentRunResult { Response = "ok", ThreadId = Guid.NewGuid() });

        var threadStoreMock = new Mock<IChannelThreadStore>();
        threadStoreMock
            .Setup(s => s.GetThreadIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((Guid?)null);

        var services = new ServiceCollection();
        services.AddSingleton(_connection);
        services.AddScoped<IDataFilterManager, DataFilterManager>();
        services.AddSingleton<ICurrentTenant>(tenantRecorder);
        services.AddSingleton(runtimeMock.Object);
        services.AddSingleton(threadStoreMock.Object);
        services.AddSingleton(new Mock<IAgentThreadService>().Object);

        services.AddScoped(sp => CreateDbContext(sp.GetService<IDataFilterManager>()));
        services.AddScoped<IRepository<SessionBindingRule, Guid>>(sp =>
            new EFCoreRepository<SessionBinderMtDbContext, SessionBindingRule, Guid>(
                sp.GetRequiredService<SessionBinderMtDbContext>()));

        // 真实 Gateway + 真实 Binder（空配置规则 → 只走数据库规则的租户分区路径）
        services.AddSingleton<IGateway>(sp =>
        {
            var gatewayOptions = new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions { DefaultAgentId = DefaultAgent });
            var binder = new DefaultSessionBinder([], gatewayOptions, sp.GetRequiredService<IServiceScopeFactory>());
            return new DefaultGateway(binder, sp.GetRequiredService<IServiceScopeFactory>(),
                gatewayOptions, NullLogger<DefaultGateway>.Instance);
        });

        var provider = services.BuildServiceProvider();

        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance,
            bus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            MsOptions.Create(new ChannelsModuleOptions { Enabled = true, MaxConcurrency = 1, DefaultAgentId = null }),
            adapters: [new FakeChannelAdapter("telegram", channelTenantId)]);

        return new Harness(manager, bus, capturedRuns, tenantRecorder);
    }

    /// <summary>
    /// 走真实入站管线：启动 manager 消费循环 → 发布入站消息 → 等待出站回复 → 返回 Runtime 收到的请求。
    /// </summary>
    private static async Task<AgentRunRequest> RunInboundAsync(Harness harness, InboundMessage message)
    {
        var outboundTcs = new TaskCompletionSource<OutboundMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await harness.Bus.SubscribeOutboundAsync(outbound =>
        {
            outboundTcs.TrySetResult(outbound);
            return Task.CompletedTask;
        });

        await harness.Manager.StartAsync();
        try
        {
            await harness.Bus.PublishInboundAsync(message);

            var completed = await Task.WhenAny(outboundTcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            completed.ShouldBe(outboundTcs.Task, "Inbound message was not processed within the timeout.");
        }
        finally
        {
            await harness.Manager.StopAsync();
        }

        harness.CapturedRuns.TryDequeue(out var captured).ShouldBeTrue("Agent runtime was never invoked.");
        return captured!;
    }

    private SessionBinderMtDbContext CreateDbContext(IDataFilterManager? dataFilterManager)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(Guid.Empty);
        currentUser.Setup(m => m.IsAuthenticated).Returns(false);
        currentUser.Setup(m => m.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<SessionBinderMtDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>()
            .Options;

        // DbContext 自身无"当前租户"（绑定器缓存加载场景）；租户上下文由处理链经 ICurrentTenant.Change 建立。
        return new SessionBinderMtDbContext(
            options,
            currentUser.Object,
            currentTenant: null,
            dataFilterManager,
            MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
    }

    // =====================================================================
    // 测试替身
    // =====================================================================

    /// <summary>模拟"配置了归属租户"的渠道适配器 - TenantId 唯一来源是 adapter（即 options）本身。</summary>
    private sealed class FakeChannelAdapter : IChannelAdapter
    {
        public FakeChannelAdapter(string name, Guid? tenantId)
        {
            Name = name;
            TenantId = tenantId;
        }

        public string Name { get; }
        public bool SupportsStreaming => false;
        public Guid? TenantId { get; }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAsync(OutboundMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>记录所有 Change 调用的 ICurrentTenant 替身（AsyncLocal 语义与框架 CurrentTenant 一致）。</summary>
    private sealed class RecordingCurrentTenant : ICurrentTenant
    {
        private readonly AsyncLocal<Guid?> _current = new();
        public ConcurrentQueue<Guid?> Changes { get; } = new();

        public Guid? Id => _current.Value;
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;

        public IDisposable Change(Guid? tenantId, string? tenantName = null)
        {
            Changes.Enqueue(tenantId);
            var previous = _current.Value;
            _current.Value = tenantId;
            return new RestoreScope(() => _current.Value = previous);
        }

        private sealed class RestoreScope : IDisposable
        {
            private readonly Action _restore;
            public RestoreScope(Action restore) => _restore = restore;
            public void Dispose() => _restore();
        }
    }
}
