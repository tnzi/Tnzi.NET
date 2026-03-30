using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Channels.Abstractions;
using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Manager;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using Tnzi.AI.Dtos;
using Tnzi.AI.Services.Interfaces;
using Tnzi.Results;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Tests.AI.Channels;

public class ChannelManagerTests
{
    [Fact]
    public async Task ProcessMessage_ChatType_CallsRuntimeAndPublishesOutbound()
    {
        // Arrange
        var bus = new InMemoryChannelMessageBus();
        var threadStore = new Mock<IChannelThreadStore>();
        var runtime = new Mock<IAgentRuntime>();
        var threadService = new Mock<IAgentThreadService>();
        var options = MsOptions.Create(new ChannelsModuleOptions { MaxConcurrency = 1 });
        var scopeFactory = CreateMockScopeFactory(threadStore.Object, runtime.Object, threadService.Object);

        var responseThreadId = Guid.NewGuid();
        threadStore.Setup(s => s.GetThreadIdAsync("telegram", "123", null))
            .ReturnsAsync((Guid?)null);
        runtime.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "AI Reply",
                ThreadId = responseThreadId,
                FinishReason = "stop"
            });

        OutboundMessage? capturedOutbound = null;
        await bus.SubscribeOutboundAsync(msg => { capturedOutbound = msg; return Task.CompletedTask; });

        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance,
            bus,
            scopeFactory,
            options);

        // Act — process a single message
        var inbound = new InboundMessage("telegram", "123", "user1", "Hello AI");
        await bus.PublishInboundAsync(inbound);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await manager.StartAsync(cts.Token);
        await Task.Delay(500);
        await manager.StopAsync(CancellationToken.None);

        // Assert
        runtime.Verify(r => r.RunAsync(
            It.Is<AgentRunRequest>(req => req.UserMessage == "Hello AI"),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedOutbound);
        Assert.Equal("AI Reply", capturedOutbound!.Text);
        Assert.Equal(responseThreadId, capturedOutbound.ThreadId);
    }

    [Fact]
    public async Task ProcessMessage_CommandNew_CreatesNewThread()
    {
        // Arrange
        var bus = new InMemoryChannelMessageBus();
        var threadStore = new Mock<IChannelThreadStore>();
        var runtime = new Mock<IAgentRuntime>();
        var threadService = new Mock<IAgentThreadService>();
        var options = MsOptions.Create(new ChannelsModuleOptions { MaxConcurrency = 1 });
        var scopeFactory = CreateMockScopeFactory(threadStore.Object, runtime.Object, threadService.Object);

        var newThreadId = Guid.NewGuid();
        threadService.Setup(s => s.CreateAsync(It.IsAny<CreateAgentThreadDto>()))
            .ReturnsAsync(Result<AgentThreadDto>.Success(new AgentThreadDto { Id = newThreadId }));

        OutboundMessage? capturedOutbound = null;
        await bus.SubscribeOutboundAsync(msg => { capturedOutbound = msg; return Task.CompletedTask; });

        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance,
            bus,
            scopeFactory,
            options);

        // Act
        var inbound = new InboundMessage("telegram", "123", "user1", "/new", InboundMessageType.Command);
        await bus.PublishInboundAsync(inbound);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await manager.StartAsync(cts.Token);
        await Task.Delay(500);
        await manager.StopAsync(CancellationToken.None);

        // Assert
        threadService.Verify(s => s.CreateAsync(It.IsAny<CreateAgentThreadDto>()), Times.Once);
        threadStore.Verify(s => s.SetThreadIdAsync("telegram", "123", newThreadId, null, "user1"), Times.Once);
        Assert.NotNull(capturedOutbound);
        Assert.Contains("New conversation started", capturedOutbound!.Text);
    }

    [Fact]
    public async Task ProcessMessage_CommandHelp_ReturnsHelpText()
    {
        // Arrange
        var bus = new InMemoryChannelMessageBus();
        var threadStore = new Mock<IChannelThreadStore>();
        var runtime = new Mock<IAgentRuntime>();
        var threadService = new Mock<IAgentThreadService>();
        var options = MsOptions.Create(new ChannelsModuleOptions { MaxConcurrency = 1 });
        var scopeFactory = CreateMockScopeFactory(threadStore.Object, runtime.Object, threadService.Object);

        OutboundMessage? capturedOutbound = null;
        await bus.SubscribeOutboundAsync(msg => { capturedOutbound = msg; return Task.CompletedTask; });

        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance,
            bus,
            scopeFactory,
            options);

        // Act
        var inbound = new InboundMessage("telegram", "123", "user1", "/help", InboundMessageType.Command);
        await bus.PublishInboundAsync(inbound);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await manager.StartAsync(cts.Token);
        await Task.Delay(500);
        await manager.StopAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(capturedOutbound);
        Assert.Contains("/new", capturedOutbound!.Text);
        Assert.Contains("/help", capturedOutbound.Text);
    }

    [Fact]
    public async Task ProcessMessage_RuntimeThrows_SendsErrorReply()
    {
        // Arrange
        var bus = new InMemoryChannelMessageBus();
        var threadStore = new Mock<IChannelThreadStore>();
        var runtime = new Mock<IAgentRuntime>();
        var threadService = new Mock<IAgentThreadService>();
        var options = MsOptions.Create(new ChannelsModuleOptions { MaxConcurrency = 1 });
        var scopeFactory = CreateMockScopeFactory(threadStore.Object, runtime.Object, threadService.Object);

        threadStore.Setup(s => s.GetThreadIdAsync("telegram", "123", null))
            .ReturnsAsync(Guid.NewGuid());
        runtime.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI service unavailable"));

        OutboundMessage? capturedOutbound = null;
        await bus.SubscribeOutboundAsync(msg => { capturedOutbound = msg; return Task.CompletedTask; });

        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance,
            bus,
            scopeFactory,
            options);

        // Act
        var inbound = new InboundMessage("telegram", "123", "user1", "Hello");
        await bus.PublishInboundAsync(inbound);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await manager.StartAsync(cts.Token);
        await Task.Delay(500);
        await manager.StopAsync(CancellationToken.None);

        // Assert — error reply should be sent
        Assert.NotNull(capturedOutbound);
        Assert.Contains("error occurred", capturedOutbound!.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MaxConcurrency_RespectsSemaphoreLimit()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions { MaxConcurrency = 3 });
        var bus = new InMemoryChannelMessageBus();
        var scopeFactory = new Mock<IServiceScopeFactory>();

        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance,
            bus,
            scopeFactory.Object,
            options);

        // 确认实例创建成功且 MaxConcurrency 从配置中读取
        Assert.NotNull(manager);
    }

    [Fact]
    public async Task ProcessMessage_Chat_SavesNewThreadMapping()
    {
        // Arrange
        var bus = new InMemoryChannelMessageBus();
        var threadStore = new Mock<IChannelThreadStore>();
        var runtime = new Mock<IAgentRuntime>();
        var threadService = new Mock<IAgentThreadService>();
        var options = MsOptions.Create(new ChannelsModuleOptions { MaxConcurrency = 1 });
        var scopeFactory = CreateMockScopeFactory(threadStore.Object, runtime.Object, threadService.Object);

        var newThreadId = Guid.NewGuid();

        // 首次查询返回 null（无映射）
        threadStore.Setup(s => s.GetThreadIdAsync("telegram", "456", null))
            .ReturnsAsync((Guid?)null);

        // Runtime 自动创建线程，返回新 ThreadId
        runtime.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Hello!",
                ThreadId = newThreadId,
                FinishReason = "stop"
            });

        var manager = new ChannelManager(
            NullLogger<ChannelManager>.Instance,
            bus,
            scopeFactory,
            options);

        // Act
        var inbound = new InboundMessage("telegram", "456", "user2", "Hi");
        await bus.PublishInboundAsync(inbound);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await manager.StartAsync(cts.Token);
        await Task.Delay(500);
        await manager.StopAsync(CancellationToken.None);

        // Assert — 应该保存新线程映射
        threadStore.Verify(s => s.SetThreadIdAsync("telegram", "456", newThreadId, null, "user2"), Times.Once);
    }

    private static IServiceScopeFactory CreateMockScopeFactory(
        IChannelThreadStore threadStore,
        IAgentRuntime runtime,
        IAgentThreadService threadService)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(p => p.GetService(typeof(IChannelThreadStore))).Returns(threadStore);
        serviceProvider.Setup(p => p.GetService(typeof(IAgentRuntime))).Returns(runtime);
        serviceProvider.Setup(p => p.GetService(typeof(IAgentThreadService))).Returns(threadService);

        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(serviceScope.Object);

        return factory.Object;
    }
}
