using Tnzi.AI.Channels.Options;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;

namespace Tnzi.AI.Tests.Channels.Gateway;

/// <summary>
/// <see cref="DefaultGateway"/> 的按 peer 归属过滤：一个 peer 只能看见 / 清理自己的会话。
/// </summary>
/// <remarks>
/// <c>GetSessionsAsync</c> / <c>GetSessionAsync</c> / <c>PruneSessionAsync</c> 是给管理端与
/// 服务端内部用的<b>无过滤</b>视图；客户端路径走 <c>GetPeerSessionsAsync</c> /
/// <c>GetPeerSessionAsync</c> / <c>PrunePeerSessionAsync</c>。刻意做成两组不同名字的方法而不是
/// 一个可空参数：可空参数的默认值是"不过滤"，忘了传就静默拿到全量。
/// </remarks>
public class GatewayPeerScopedSessionTests
{
    private static readonly Guid AgentA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AgentB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>
    /// 建一个 gateway 并把两条会话真的跑进 _activeSessions（TrackSession 是 private，
    /// 只能经真实的 ProcessAsync 路径填充 —— 手工注入会把"会话是怎么被记下的"这一半
    /// 一起绕过去）。
    /// </summary>
    private static async Task<DefaultGateway> CreateGatewayWithSessionsAsync(
        params (string Channel, string ChatId, string UserId, Guid AgentId)[] sessions)
    {
        var runtimeMock = new Mock<IAgentRuntime>();
        runtimeMock
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "ok", ThreadId = Guid.NewGuid() });

        var threadStoreMock = new Mock<IChannelThreadStore>();
        threadStoreMock
            .Setup(s => s.GetThreadIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((Guid?)null);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IAgentRuntime))).Returns(runtimeMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IChannelThreadStore))).Returns(threadStoreMock.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var options = new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions
        {
            DefaultAgentId = AgentA,
            DefaultScope = SessionScope.PerChannelPeer
        });
        var binder = new DefaultSessionBinder([], options);
        var gateway = new DefaultGateway(binder, scopeFactoryMock.Object, options, NullLogger<DefaultGateway>.Instance);

        foreach (var (channel, chatId, userId, agentId) in sessions)
        {
            await gateway.ProcessAsync(new GatewayRequest
            {
                Channel = channel,
                ChatId = chatId,
                UserId = userId,
                UserMessage = "hello",
                AgentId = agentId
            });
        }

        return gateway;
    }

    [Fact]
    public async Task GetPeerSessions_ReturnsOnlyTheCallersOwnSessions()
    {
        var gateway = await CreateGatewayWithSessionsAsync(
            ("web", "alice", "alice", AgentA),
            ("web", "bob", "bob", AgentA));

        var alice = await gateway.GetPeerSessionsAsync("alice");

        alice.Count.ShouldBe(1);
        alice[0].PeerId.ShouldBe("alice");
    }

    [Fact]
    public async Task GetSessions_WithoutPeer_StillReturnsEverything_ForAdminUse()
    {
        // 管理端视图刻意保持无过滤 —— 这条钉住"修复没有顺手改掉管理端能力"。
        var gateway = await CreateGatewayWithSessionsAsync(
            ("web", "alice", "alice", AgentA),
            ("web", "bob", "bob", AgentA));

        var all = await gateway.GetSessionsAsync();

        all.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetPeerSessions_HonoursTheAgentFilterToo()
    {
        var gateway = await CreateGatewayWithSessionsAsync(
            ("web", "alice", "alice", AgentA),
            ("telegram", "alice", "alice", AgentB));

        var onlyB = await gateway.GetPeerSessionsAsync("alice", AgentB.ToString());

        onlyB.Count.ShouldBe(1);
        onlyB[0].AgentId.ShouldBe(AgentB);
    }

    [Fact]
    public async Task GetPeerSession_AnotherPeersKey_AnswersNull()
    {
        var gateway = await CreateGatewayWithSessionsAsync(
            ("web", "alice", "alice", AgentA),
            ("web", "bob", "bob", AgentA));

        var bobKey = (await gateway.GetPeerSessionsAsync("bob")).Single().SessionKey;

        // 会话键是可推导的，所以"我知道这个键"绝不能等于"我可以读这条会话"。
        (await gateway.GetPeerSessionAsync("alice", bobKey)).ShouldBeNull();
        (await gateway.GetPeerSessionAsync("bob", bobKey)).ShouldNotBeNull();
    }

    [Fact]
    public async Task PrunePeerSession_AnotherPeersKey_DoesNotRemoveIt()
    {
        var gateway = await CreateGatewayWithSessionsAsync(
            ("web", "alice", "alice", AgentA),
            ("web", "bob", "bob", AgentA));

        var bobKey = (await gateway.GetPeerSessionsAsync("bob")).Single().SessionKey;

        var pruned = await gateway.PrunePeerSessionAsync("alice", bobKey);

        pruned.ShouldBeFalse();
        (await gateway.GetSessionsAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task PrunePeerSession_OwnKey_RemovesItAndReportsTrue()
    {
        var gateway = await CreateGatewayWithSessionsAsync(
            ("web", "alice", "alice", AgentA),
            ("web", "bob", "bob", AgentA));

        var bobKey = (await gateway.GetPeerSessionsAsync("bob")).Single().SessionKey;

        (await gateway.PrunePeerSessionAsync("bob", bobKey)).ShouldBeTrue();
        (await gateway.GetSessionsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task PrunePeerSession_UnknownKey_ReportsFalse_SameAsNotOwned()
    {
        // 不属于我的键与根本不存在的键必须给同一个答案，否则按键探测就能确认会话存在。
        var gateway = await CreateGatewayWithSessionsAsync(("web", "alice", "alice", AgentA));

        (await gateway.PrunePeerSessionAsync("alice", "agent:zzz:peer:nobody")).ShouldBeFalse();
    }
}
