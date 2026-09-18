using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Manager;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// 出站回复必须带上入站消息的线程指针（<c>ThreadTs</c> / <c>TopicId</c> / <c>Metadata</c>）：
/// 适配器两端都测过（入站抓取、出站读取），中间的 <see cref="ChannelManager"/> 却一处都没传 ——
/// Telegram 论坛话题里的提问被回到 General，Slack 线程里的提问被回到频道顶层，而日志全是成功。
/// </summary>
public sealed class ChannelManagerReplyContextTests
{
    private static readonly Guid AgentId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    [Fact]
    public async Task Chat_Reply_CarriesInboundThreadTsTopicIdAndMetadata()
    {
        var harness = CreateHarness(withGateway: true);
        var metadata = new Dictionary<string, object> { ["interactionToken"] = "tok" };

        var outbound = await RunInboundAsync(harness,
            new InboundMessage("telegram", "c1", "u1", "hello", ThreadTs: "T1", TopicId: "42", Metadata: metadata));

        outbound.ThreadTs.ShouldBe("T1");
        outbound.TopicId.ShouldBe("42");
        outbound.Metadata.ShouldBe(metadata);
        (await harness.Gateway!.GetSessionsAsync()).Count.ShouldBe(1, "reply must have gone through the gateway path");
    }

    [Fact]
    public async Task Chat_DirectRuntimeFallback_CarriesInboundThreadTsAndTopicId()
    {
        var harness = CreateHarness(withGateway: false);

        var outbound = await RunInboundAsync(harness,
            new InboundMessage("telegram", "c1", "u1", "hello", ThreadTs: "T1", TopicId: "42"));

        outbound.ThreadTs.ShouldBe("T1");
        outbound.TopicId.ShouldBe("42");
    }

    [Theory]
    [InlineData("/new")]
    [InlineData("/status")]
    [InlineData("/help")]
    [InlineData("/models")]
    [InlineData("/memory")]
    [InlineData("/unknown")]
    public async Task Command_Reply_CarriesInboundThreadTsAndTopicId(string command)
    {
        var harness = CreateHarness(withGateway: true);

        var outbound = await RunInboundAsync(harness,
            new InboundMessage("telegram", "c1", "u1", command, InboundMessageType.Command, ThreadTs: "T1", TopicId: "42"));

        outbound.ThreadTs.ShouldBe("T1");
        outbound.TopicId.ShouldBe("42");
    }

    [Fact]
    public async Task ErrorReply_CarriesInboundThreadTsAndTopicId()
    {
        var harness = CreateHarness(withGateway: false, runtimeThrows: true);

        var outbound = await RunInboundAsync(harness,
            new InboundMessage("telegram", "c1", "u1", "hello", ThreadTs: "T1", TopicId: "42"));

        outbound.Text.ShouldContain("error");
        outbound.ThreadTs.ShouldBe("T1");
        outbound.TopicId.ShouldBe("42");
    }

    private sealed record Harness(ChannelManager Manager, InMemoryChannelMessageBus Bus, IGateway? Gateway);

    private static Harness CreateHarness(bool withGateway, bool runtimeThrows = false)
    {
        var runtimeMock = new Mock<IAgentRuntime>();
        var runSetup = runtimeMock.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()));
        if (runtimeThrows)
            runSetup.ThrowsAsync(new InvalidOperationException("boom"));
        else
            runSetup.ReturnsAsync(new AgentRunResult { Response = "ok", ThreadId = Guid.NewGuid() });

        var threadStoreMock = new Mock<IChannelThreadStore>();
        threadStoreMock
            .Setup(s => s.GetThreadIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((Guid?)null);

        var threadServiceMock = new Mock<IAgentThreadService>();
        threadServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateAgentThreadDto>()))
            .ReturnsAsync(Result<AgentThreadDto>.Success(new AgentThreadDto { Id = Guid.NewGuid() }));

        var services = new ServiceCollection();
        services.AddSingleton(runtimeMock.Object);
        services.AddSingleton(threadStoreMock.Object);
        services.AddSingleton(threadServiceMock.Object);

        if (withGateway)
        {
            var gatewayOptions = new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions { DefaultAgentId = AgentId });
            services.AddSingleton<IGateway>(sp =>
                new DefaultGateway(new DefaultSessionBinder([], gatewayOptions),
                    sp.GetRequiredService<IServiceScopeFactory>(), gatewayOptions, NullLogger<DefaultGateway>.Instance));
        }

        var provider = services.BuildServiceProvider();
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance,
            bus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            MsOptions.Create(new ChannelsModuleOptions { Enabled = true, MaxConcurrency = 1, DefaultAgentId = AgentId }));

        return new Harness(manager, bus, withGateway ? provider.GetRequiredService<IGateway>() : null);
    }

    private static async Task<OutboundMessage> RunInboundAsync(Harness harness, InboundMessage message)
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
            completed.ShouldBe(outboundTcs.Task, "Inbound message was not answered within the timeout.");
        }
        finally
        {
            await harness.Manager.StopAsync();
        }

        return await outboundTcs.Task;
    }
}
