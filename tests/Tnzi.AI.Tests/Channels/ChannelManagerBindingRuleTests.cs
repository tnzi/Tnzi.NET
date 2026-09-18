using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Entities;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;
using Tnzi.AI.Channels.Manager;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// 会话绑定规则在 IM 入站流量下的优先级门禁：
/// <c>AI:Channels:DefaultAgentId</c> 是<b>兜底</b>不是显式指定 —— 配置了它之后，
/// 绑定规则（配置 + 数据库）仍必须先于它命中。
/// <para>
/// 此前 <see cref="ChannelManager"/> 把该值当 <c>GatewayRequest.AgentId</c>（显式目标）送进 Gateway，
/// <see cref="DefaultSessionBinder"/> 遇到显式 Agent 直接短路，所有规则一条都不评估；
/// 唯一的端到端测试跑在 <c>DefaultAgentId = null</c> 的配置上，正好是缺陷显不出来的那一种。
/// </para>
/// </summary>
public sealed class ChannelManagerBindingRuleTests
{
    private static readonly Guid ChannelDefaultAgent = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid RuleAgent = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid GatewayDefaultAgent = Guid.Parse("dddddddd-0000-0000-0000-000000000003");

    [Fact]
    public async Task Inbound_WithChannelDefaultAgentSet_MatchingRuleStillWins()
    {
        var rule = new SessionBindingRule
        {
            Channel = "telegram", AgentId = RuleAgent, Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = true
        };
        var harness = CreateHarness(channelDefaultAgentId: ChannelDefaultAgent, configRules: [rule]);

        var captured = await RunInboundAsync(harness, new InboundMessage("telegram", "c1", "u1", "hello"));

        captured.AgentId.ShouldBe(RuleAgent,
            "A binding rule MUST win over AI:Channels:DefaultAgentId; the channel default is a fallback, not an explicit target.");
        harness.GatewaySessions.Count.ShouldBe(1, "The reply must have gone through the gateway path.");
    }

    [Fact]
    public async Task Inbound_WithChannelDefaultAgentSet_NoRule_UsesChannelDefault()
    {
        var harness = CreateHarness(channelDefaultAgentId: ChannelDefaultAgent, configRules: []);

        var captured = await RunInboundAsync(harness, new InboundMessage("telegram", "c1", "u1", "hello"));

        captured.AgentId.ShouldBe(ChannelDefaultAgent,
            "With no matching rule the channel default beats the gateway default.");
        harness.GatewaySessions.Count.ShouldBe(1, "The reply must have gone through the gateway path, not the direct-runtime fallback.");
    }

    [Fact]
    public async Task Inbound_WithoutChannelDefaultAgent_NoRule_UsesGatewayDefault()
    {
        var harness = CreateHarness(channelDefaultAgentId: null, configRules: []);

        var captured = await RunInboundAsync(harness, new InboundMessage("telegram", "c1", "u1", "hello"));

        captured.AgentId.ShouldBe(GatewayDefaultAgent);
    }

    [Fact]
    public async Task NewCommand_ThenChat_ThreadIsCreatedForTheRuleAgentAndReused()
    {
        // /new（Telegram 客户端首次接触会自动发 /start）此前直接为渠道默认 Agent 建线程并写入映射：
        // 下一条消息经 Gateway 命中规则 Agent B，却拿到为 A 建的线程，AgentThreadService 以「Agent 不匹配」拒绝，
        // Gateway 报错、ChannelManager 回退到直连 Runtime 用渠道默认 Agent 跑 —— 规则在管理端显示启用，
        // 这个 peer 却从此一直被路由到渠道默认 Agent，唯一痕迹是一条 Warning 日志。
        var rule = new SessionBindingRule
        {
            Channel = "telegram", AgentId = RuleAgent, Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = true
        };
        var harness = CreateHarness(channelDefaultAgentId: ChannelDefaultAgent, configRules: [rule]);

        var newReply = await RunInboundAsync(harness,
            new InboundMessage("telegram", "c1", "u1", "/new", InboundMessageType.Command), expectRun: false);
        newReply.Text.ShouldContain("New conversation started");
        var created = harness.CreatedThreads.ShouldHaveSingleItem();
        created.AgentId.ShouldBe(RuleAgent, "/new must create the thread for the agent the binding rules resolve, not the channel default");

        await RunInboundAsync(harness, new InboundMessage("telegram", "c1", "u1", "hello"), expectRun: true);

        harness.CapturedRuns.TryDequeue(out var run).ShouldBeTrue();
        run!.AgentId.ShouldBe(RuleAgent);
        run.ThreadId.ShouldBe(harness.ThreadStore.LastAssignedThreadId, "the chat must continue on the thread /new created");
        harness.GatewaySessions.Count.ShouldBe(1, "The reply must have gone through the gateway path.");
    }

    [Fact]
    public async Task NewCommand_NoRule_ThreadIsCreatedForTheChannelDefault()
    {
        var harness = CreateHarness(channelDefaultAgentId: ChannelDefaultAgent, configRules: []);

        await RunInboundAsync(harness,
            new InboundMessage("telegram", "c1", "u1", "/start", InboundMessageType.Command), expectRun: false);

        harness.CreatedThreads.ShouldHaveSingleItem().AgentId.ShouldBe(ChannelDefaultAgent);
    }

    [Fact]
    public async Task Chat_GatewayFails_DoesNotRerunThePeerOnTheChannelDefault()
    {
        // Gateway 存在而失败时回退到直连 Runtime + 渠道默认 Agent = 把一个规则绑定的 peer 静默改道到别的 Agent，
        // 还把同一条消息跑第二遍。回退只属于「没有装 Gateway」的部署；Gateway 失败要把失败告诉用户。
        var rule = new SessionBindingRule
        {
            Channel = "telegram", AgentId = RuleAgent, Scope = SessionScope.PerPeer, Priority = 10, IsEnabled = true
        };
        var harness = CreateHarness(channelDefaultAgentId: ChannelDefaultAgent, configRules: [rule], runtimeThrows: true);

        var reply = await RunInboundAsync(harness, new InboundMessage("telegram", "c1", "u1", "hello"), expectRun: true);

        reply.Text.ShouldContain("error");
        harness.CapturedRuns.Count.ShouldBe(1, "the message must not be run a second time through the direct runtime");
        harness.CapturedRuns.TryDequeue(out var run);
        run!.AgentId.ShouldBe(RuleAgent, "the only run is the gateway one, on the rule agent");
    }

    // =====================================================================
    // 装配 - 真实 ChannelManager + 真实 Bus + 真实 DefaultGateway + 真实 DefaultSessionBinder（配置规则）
    // =====================================================================

    private sealed record Harness(
        ChannelManager Manager,
        InMemoryChannelMessageBus Bus,
        ConcurrentQueue<AgentRunRequest> CapturedRuns,
        IGateway Gateway,
        InMemoryThreadStore ThreadStore,
        List<CreateAgentThreadDto> CreatedThreads)
    {
        public IReadOnlyList<GatewaySession> GatewaySessions => Gateway.GetSessionsAsync().GetAwaiter().GetResult();
    }

    /// <summary>记得住映射的线程存储：/new 写进去的线程，下一条消息必须读得回来。</summary>
    private sealed class InMemoryThreadStore : IChannelThreadStore
    {
        private readonly ConcurrentDictionary<string, Guid> _map = new();

        public Guid? LastAssignedThreadId { get; private set; }

        private static string Key(string channel, string chatId, string? topicId) => $"{channel}|{chatId}|{topicId}";

        public Task<Guid?> GetThreadIdAsync(string channelName, string chatId, string? topicId = null)
            => Task.FromResult(_map.TryGetValue(Key(channelName, chatId, topicId), out var id) ? id : (Guid?)null);

        public Task SetThreadIdAsync(string channelName, string chatId, Guid threadId, string? topicId = null, string? userId = null)
        {
            _map[Key(channelName, chatId, topicId)] = threadId;
            LastAssignedThreadId = threadId;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string channelName, string chatId, string? topicId = null)
        {
            _map.TryRemove(Key(channelName, chatId, topicId), out _);
            return Task.CompletedTask;
        }
    }

    private static Harness CreateHarness(Guid? channelDefaultAgentId, IReadOnlyList<SessionBindingRule> configRules, bool runtimeThrows = false)
    {
        var capturedRuns = new ConcurrentQueue<AgentRunRequest>();

        var runtimeMock = new Mock<IAgentRuntime>();
        var runSetup = runtimeMock
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunRequest, CancellationToken>((req, _) => capturedRuns.Enqueue(req));
        if (runtimeThrows)
        {
            runSetup.ThrowsAsync(new InvalidOperationException("boom"));
        }
        else
        {
            runSetup.ReturnsAsync((AgentRunRequest req, CancellationToken _) =>
                new AgentRunResult { Response = "ok", ThreadId = req.ThreadId ?? Guid.NewGuid() });
        }

        var threadStore = new InMemoryThreadStore();
        var createdThreads = new List<CreateAgentThreadDto>();
        var threadServiceMock = new Mock<IAgentThreadService>();
        threadServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateAgentThreadDto>()))
            .ReturnsAsync((CreateAgentThreadDto input) =>
            {
                createdThreads.Add(input);
                return Result<AgentThreadDto>.Success(new AgentThreadDto { Id = Guid.NewGuid(), AgentId = input.AgentId, Title = input.Title });
            });

        var services = new ServiceCollection();
        services.AddSingleton(runtimeMock.Object);
        services.AddSingleton<IChannelThreadStore>(threadStore);
        services.AddSingleton(threadServiceMock.Object);

        var gatewayOptions = new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions { DefaultAgentId = GatewayDefaultAgent });
        services.AddSingleton<ISessionBinder>(new DefaultSessionBinder(configRules, gatewayOptions));
        services.AddSingleton<IGateway>(sp => new DefaultGateway(sp.GetRequiredService<ISessionBinder>(),
            sp.GetRequiredService<IServiceScopeFactory>(), gatewayOptions, NullLogger<DefaultGateway>.Instance));

        var provider = services.BuildServiceProvider();

        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance,
            bus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            MsOptions.Create(new ChannelsModuleOptions
            {
                Enabled = true, MaxConcurrency = 1, DefaultAgentId = channelDefaultAgentId
            }));

        return new Harness(manager, bus, capturedRuns, provider.GetRequiredService<IGateway>(), threadStore, createdThreads);
    }

    private static async Task<AgentRunRequest> RunInboundAsync(Harness harness, InboundMessage message)
    {
        await RunInboundAsync(harness, message, expectRun: true);
        harness.CapturedRuns.TryDequeue(out var captured).ShouldBeTrue("Agent runtime was never invoked.");
        return captured!;
    }

    /// <summary>发一条入站消息并等它的第一条回复；<paramref name="expectRun"/> 为 false 时断言运行时没被调用。</summary>
    private static async Task<OutboundMessage> RunInboundAsync(Harness harness, InboundMessage message, bool expectRun)
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

        if (!expectRun)
        {
            harness.CapturedRuns.ShouldBeEmpty("a command must not invoke the agent runtime");
        }

        return await outboundTcs.Task;
    }
}
