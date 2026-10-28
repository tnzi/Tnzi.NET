using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Manager;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// Built-in channel commands are recognised by their name, the way IM clients actually send them:
/// Telegram appends the bot username to commands picked from the group command menu
/// (<c>/new@MyBot</c>) and deep links carry a payload (<c>/start payload</c>).
/// </summary>
public sealed class ChannelManagerCommandTests
{
    private static readonly Guid AgentId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid MappedThreadId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Theory]
    [InlineData("/new@MyBot")]
    [InlineData("/start deep-link-payload")]
    [InlineData("  /NEW  ")]
    public async Task NewConversation_IsRecognisedWithBotMentionOrArguments(string text)
    {
        var harness = CreateHarness();

        var outbound = await RunCommandAsync(harness, text);

        outbound.Text.ShouldBe("New conversation started. How can I help you?");
        harness.ThreadService.Verify(s => s.CreateAsync(It.IsAny<CreateAgentThreadDto>()), Times.Once);
    }

    [Fact]
    public async Task Status_IsRecognisedWithTrailingArguments()
    {
        var harness = CreateHarness();

        var outbound = await RunCommandAsync(harness, "  /STATUS  extra");

        outbound.Text.ShouldBe($"Active thread: {MappedThreadId:N}");
    }

    [Fact]
    public async Task UnknownCommand_WithBotMention_StillAnswersUnknown()
    {
        var harness = CreateHarness();

        var outbound = await RunCommandAsync(harness, "/foo@MyBot");

        outbound.Text.ShouldBe("Unknown command: /foo@MyBot. Send /help for available commands.");
    }

    // /memory used to search the shared "default" memory bucket with an empty query, which the
    // store rejects, so every call ended in the generic error reply. An IM peer has no memory
    // scope of its own, so the command is gone rather than pointed at another bucket.
    [Fact]
    public async Task Memory_IsNotACommand_AndNeverReadsTheMemoryStore()
    {
        var memoryStore = new Mock<IMemoryStore>(MockBehavior.Strict);
        var harness = CreateHarness(memoryStore.Object);

        var outbound = await RunCommandAsync(harness, "/memory");

        outbound.Text.ShouldBe("Unknown command: /memory. Send /help for available commands.");
        memoryStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Help_ListsOnlyTheCommandsThatExist()
    {
        var harness = CreateHarness();

        var outbound = await RunCommandAsync(harness, "/help@MyBot");

        outbound.Text.ShouldStartWith("Commands:");
        outbound.Text.ShouldContain("/new");
        outbound.Text.ShouldContain("/status");
        outbound.Text.ShouldContain("/models");
        outbound.Text.ShouldNotContain("/memory");
    }

    private sealed record Harness(ChannelManager Manager, InMemoryChannelMessageBus Bus, Mock<IAgentThreadService> ThreadService);

    private static Harness CreateHarness(IMemoryStore? memoryStore = null)
    {
        var threadStore = new Mock<IChannelThreadStore>();
        threadStore
            .Setup(s => s.GetThreadIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(MappedThreadId);

        var threadService = new Mock<IAgentThreadService>();
        threadService
            .Setup(s => s.CreateAsync(It.IsAny<CreateAgentThreadDto>()))
            .ReturnsAsync(Result<AgentThreadDto>.Success(new AgentThreadDto { Id = Guid.NewGuid() }));

        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IAgentRuntime>().Object);
        services.AddSingleton(threadStore.Object);
        services.AddSingleton(threadService.Object);
        if (memoryStore != null)
        {
            services.AddSingleton(memoryStore);
        }

        var provider = services.BuildServiceProvider();
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance,
            bus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            MsOptions.Create(new ChannelsModuleOptions { Enabled = true, MaxConcurrency = 1, DefaultAgentId = AgentId }));

        return new Harness(manager, bus, threadService);
    }

    private static async Task<OutboundMessage> RunCommandAsync(Harness harness, string text)
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
            await harness.Bus.PublishInboundAsync(
                new InboundMessage("telegram", "c1", "u1", text, InboundMessageType.Command));
            var completed = await Task.WhenAny(outboundTcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            completed.ShouldBe(outboundTcs.Task, "Command was not answered within the timeout.");
        }
        finally
        {
            await harness.Manager.StopAsync();
        }

        return await outboundTcs.Task;
    }
}
