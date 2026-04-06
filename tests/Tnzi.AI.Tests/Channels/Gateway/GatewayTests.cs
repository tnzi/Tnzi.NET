using Tnzi.AI.Channels.Abstractions;
using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;
using Tnzi.AI.Channels.Options;

namespace Tnzi.AI.Tests.Channels.Gateway;

public class GatewayTests
{
    private static readonly Guid TestAgentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TestThreadId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static GatewayRequest CreateRequest(
        string channel = "telegram",
        string chatId = "chat-1",
        string userId = "user-1",
        string message = "Hello",
        Guid? agentId = null,
        Guid? threadId = null)
    {
        return new GatewayRequest
        {
            Channel = channel,
            ChatId = chatId,
            UserId = userId,
            UserMessage = message,
            AgentId = agentId,
            ThreadId = threadId
        };
    }

    private static (DefaultGateway gateway, Mock<IAgentRuntime> runtime, Mock<IChannelThreadStore> threadStore) CreateGateway(
        Guid? defaultAgentId = null,
        Guid? existingThreadId = null)
    {
        var agentId = defaultAgentId ?? TestAgentId;

        // SessionBinder setup
        var options = Microsoft.Extensions.Options.Options.Create(new GatewayOptions
        {
            DefaultAgentId = agentId
        });
        var binder = new DefaultSessionBinder([], options);

        // Scoped service mocks
        var runtimeMock = new Mock<IAgentRuntime>();
        var threadStoreMock = new Mock<IChannelThreadStore>();

        threadStoreMock
            .Setup(s => s.GetThreadIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(existingThreadId);

        // DI scope mock
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IAgentRuntime)))
            .Returns(runtimeMock.Object);
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IChannelThreadStore)))
            .Returns(threadStoreMock.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var gatewayOptions = Microsoft.Extensions.Options.Options.Create(new GatewayOptions
        {
            DefaultAgentId = agentId
        });
        var gateway = new DefaultGateway(binder, scopeFactoryMock.Object, gatewayOptions, NullLogger<DefaultGateway>.Instance);

        return (gateway, runtimeMock, threadStoreMock);
    }

    [Fact]
    public async Task ProcessAsync_RoutesToAgent_ReturnsResponse()
    {
        // Arrange
        var (gateway, runtime, _) = CreateGateway();
        runtime
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult
            {
                Response = "Hi there!",
                ThreadId = TestThreadId
            });

        // Act
        var response = await gateway.ProcessAsync(CreateRequest());

        // Assert
        response.Success.ShouldBeTrue();
        response.Response.ShouldBe("Hi there!");
        response.ThreadId.ShouldBe(TestThreadId);
        runtime.Verify(r => r.RunAsync(
            It.Is<AgentRunRequest>(req => req.AgentId == TestAgentId && req.UserMessage == "Hello"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NoDefaultAgent_ReturnsError()
    {
        // Arrange — DefaultAgentId = null → binder returns Guid.Empty
        var options = Microsoft.Extensions.Options.Options.Create(new GatewayOptions
        {
            DefaultAgentId = null
        });
        var binder = new DefaultSessionBinder([], options);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var gatewayOptions = Microsoft.Extensions.Options.Options.Create(new GatewayOptions
        {
            DefaultAgentId = null
        });
        var gateway = new DefaultGateway(binder, scopeFactoryMock.Object, gatewayOptions, NullLogger<DefaultGateway>.Instance);

        // Act
        var response = await gateway.ProcessAsync(CreateRequest());

        // Assert
        response.Success.ShouldBeFalse();
        response.Error.ShouldBe("No agent configured for this binding.");
    }

    [Fact]
    public async Task ProcessAsync_ExplicitAgentId_UsesIt()
    {
        // Arrange
        var explicitAgentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var (gateway, runtime, _) = CreateGateway();
        runtime
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "OK", ThreadId = TestThreadId });

        // Act
        var response = await gateway.ProcessAsync(CreateRequest(agentId: explicitAgentId));

        // Assert
        response.Success.ShouldBeTrue();
        runtime.Verify(r => r.RunAsync(
            It.Is<AgentRunRequest>(req => req.AgentId == explicitAgentId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSessionsAsync_ReturnsEmptyInitially()
    {
        // Arrange
        var (gateway, _, _) = CreateGateway();

        // Act
        var sessions = await gateway.GetSessionsAsync();

        // Assert
        sessions.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ProcessAsync_TracksActiveSession()
    {
        // Arrange
        var (gateway, runtime, _) = CreateGateway();
        runtime
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "OK", ThreadId = TestThreadId });

        // Act
        await gateway.ProcessAsync(CreateRequest());
        var sessions = await gateway.GetSessionsAsync();

        // Assert
        sessions.Count.ShouldBe(1);
        sessions[0].AgentId.ShouldBe(TestAgentId);
        sessions[0].ThreadId.ShouldBe(TestThreadId);
        sessions[0].Channel.ShouldBe("telegram");
    }

    [Fact]
    public async Task PruneSessionAsync_RemovesSession()
    {
        // Arrange
        var (gateway, runtime, _) = CreateGateway();
        runtime
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult { Response = "OK", ThreadId = TestThreadId });

        await gateway.ProcessAsync(CreateRequest());
        var sessions = await gateway.GetSessionsAsync();
        sessions.Count.ShouldBe(1);

        // Act
        await gateway.PruneSessionAsync(sessions[0].SessionKey);

        // Assert
        var afterPrune = await gateway.GetSessionsAsync();
        afterPrune.Count.ShouldBe(0);
    }
}
