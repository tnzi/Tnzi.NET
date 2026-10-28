using System.Net;
using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

public class McpAuthRecoveryToolWrapperTests
{
    private readonly Mock<ILogger<McpAuthRecoveryToolWrapper>> _logger = new();

    #region IsAuthFailure Detection

    [Theory]
    [InlineData("HTTP 401 Unauthorized")]
    [InlineData("Server returned 403 Forbidden")]
    [InlineData("Unauthorized access to resource")]
    [InlineData("Forbidden: insufficient permissions")]
    [InlineData("authentication failed for user")]
    [InlineData("token expired, please re-authenticate")]
    [InlineData("error: invalid_token")]
    [InlineData("OAuth error: invalid_grant")]
    public void IsAuthFailure_WithAuthRelatedMessage_ReturnsTrue(string message)
    {
        var ex = new InvalidOperationException(message);
        McpAuthRecoveryToolWrapper.IsAuthFailure(ex).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Connection refused")]
    [InlineData("Network timeout")]
    [InlineData("Internal server error")]
    [InlineData("Bad gateway")]
    [InlineData("Order 4031 was not found")]
    [InlineData("Request id 14010 failed with a timeout")]
    public void IsAuthFailure_WithNonAuthMessage_ReturnsFalse(string message)
    {
        var ex = new InvalidOperationException(message);
        McpAuthRecoveryToolWrapper.IsAuthFailure(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsAuthFailure_WithHttpRequestException401_ReturnsTrue()
    {
        var ex = new HttpRequestException("Request failed", null, HttpStatusCode.Unauthorized);
        McpAuthRecoveryToolWrapper.IsAuthFailure(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsAuthFailure_WithHttpRequestException403_ReturnsTrue()
    {
        var ex = new HttpRequestException("Request failed", null, HttpStatusCode.Forbidden);
        McpAuthRecoveryToolWrapper.IsAuthFailure(ex).ShouldBeTrue();
    }

    [Fact]
    public void IsAuthFailure_WithHttpRequestException500_ReturnsFalse()
    {
        var ex = new HttpRequestException("Request failed", null, HttpStatusCode.InternalServerError);
        McpAuthRecoveryToolWrapper.IsAuthFailure(ex).ShouldBeFalse();
    }

    [Fact]
    public void IsAuthFailure_WithNestedAuthException_ReturnsTrue()
    {
        var inner = new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
        var outer = new InvalidOperationException("MCP tool call failed", inner);
        McpAuthRecoveryToolWrapper.IsAuthFailure(outer).ShouldBeTrue();
    }

    #endregion

    #region InvokeCoreAsync - Successful Call

    [Fact]
    public async Task InvokeCoreAsync_SuccessfulCall_ReturnsResultWithoutRetry()
    {
        // Arrange
        var callCount = 0;
        var innerFunction = CreateMockFunction(() =>
        {
            callCount++;
            return "success";
        });

        var wrapper = CreateWrapper(innerFunction);

        // Act
        var result = await wrapper.InvokeAsync(new AIFunctionArguments());

        // Assert
        result?.ToString().ShouldBe("success");
        callCount.ShouldBe(1);
    }

    #endregion

    #region InvokeCoreAsync - Auth Failure Recovery

    [Fact]
    public async Task InvokeCoreAsync_AuthFailure_InvalidatesTokenAndRetries()
    {
        // Arrange
        var innerCalls = 0;
        var innerFunction = CreateMockFunction(() =>
        {
            innerCalls++;
            throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
        });

        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = CreateReconnectingFactory(() => "retry-success");
        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var wrapper = new McpAuthRecoveryToolWrapper(
            innerFunction, Server, oauthHandler, clientFactory.Object, toolProvider, _logger.Object);

        // Act
        var result = await wrapper.InvokeAsync(new AIFunctionArguments());

        // Assert
        result?.ToString().ShouldBe("retry-success");
        innerCalls.ShouldBe(1);
        oauthHandler.InvalidateCount.ShouldBe(1);
        oauthHandler.InvalidatedServers.ShouldContain("test-server");
        clientFactory.Verify(x => x.InvalidateClientAsync("test-server", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// SDK 的 McpClientTool 绑在列出它的那个客户端上，而恢复流程刚把那个客户端释放掉：
    /// 在原函数上重试必然失败（此前每次都以「Token was refreshed but the retry also failed」收尾）。
    /// 重试要到重连后的客户端上，按服务器侧原名找到同一个工具再调。
    /// </summary>
    [Fact]
    public async Task InvokeCoreAsync_AuthFailure_RetriesOnTheReconnectedClient_NotTheDisposedOne()
    {
        var disposed = false;
        var innerFunction = CreateMockFunction(() =>
        {
            if (disposed)
            {
                throw new ObjectDisposedException("McpClient");
            }
            throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
        });

        var clientFactory = CreateReconnectingFactory(() => "fresh-connection");
        clientFactory.Setup(x => x.InvalidateClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => disposed = true)
            .Returns(Task.CompletedTask);

        var wrapper = new McpAuthRecoveryToolWrapper(
            innerFunction, Server, new TrackingOAuthHandler(), clientFactory.Object, CreateMockToolProvider(clientFactory.Object), _logger.Object);

        var result = await wrapper.InvokeAsync(new AIFunctionArguments());

        result?.ToString().ShouldBe("fresh-connection");
        clientFactory.Verify(x => x.GetOrCreateClientAsync(Server, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeCoreAsync_AuthFailure_ToolGoneAfterReconnect_ThrowsWithClearMessage()
    {
        var innerFunction = CreateMockFunction(() => throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));
        var adapter = new Mock<IMcpClientAdapter>();
        adapter.Setup(a => a.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AITool> { AIFunctionFactory.Create(() => "x", "another-tool") });
        var clientFactory = new Mock<IMcpClientFactory>();
        clientFactory.Setup(x => x.InvalidateClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        clientFactory.Setup(x => x.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>())).ReturnsAsync(adapter.Object);

        var wrapper = new McpAuthRecoveryToolWrapper(
            innerFunction, Server, new TrackingOAuthHandler(), clientFactory.Object, CreateMockToolProvider(clientFactory.Object), _logger.Object);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => wrapper.InvokeAsync(new AIFunctionArguments()).AsTask());
        ex.InnerException.ShouldNotBeNull().Message.ShouldContain("no longer offers the tool 'test-tool'");
    }

    [Fact]
    public async Task InvokeCoreAsync_AuthFailureThenRetryFails_ThrowsWithClearMessage()
    {
        // Arrange - both calls fail with auth error
        var innerFunction = CreateMockFunction(() =>
        {
            throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
        });

        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = CreateReconnectingFactory(() => throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));

        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var wrapper = new McpAuthRecoveryToolWrapper(
            innerFunction, Server, oauthHandler, clientFactory.Object, toolProvider, _logger.Object);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => wrapper.InvokeAsync(new AIFunctionArguments()).AsTask());
        ex.Message.ShouldContain("test-server");
        ex.Message.ShouldContain("authentication error");
        ex.InnerException.ShouldNotBeNull();
    }

    [Fact]
    public async Task InvokeCoreAsync_NonAuthFailure_DoesNotRetry()
    {
        // Arrange
        var callCount = 0;
        var innerFunction = CreateMockFunction(() =>
        {
            callCount++;
            throw new InvalidOperationException("Connection timeout");
        });

        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = new Mock<IMcpClientFactory>();
        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var wrapper = new McpAuthRecoveryToolWrapper(
            innerFunction, Server, oauthHandler, clientFactory.Object, toolProvider, _logger.Object);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => wrapper.InvokeAsync(new AIFunctionArguments()).AsTask());
        callCount.ShouldBe(1);
        oauthHandler.InvalidateCount.ShouldBe(0);
    }

    [Fact]
    public async Task InvokeCoreAsync_AuthFailure_InvalidatesToolCache()
    {
        // Arrange
        var innerFunction = CreateMockFunction(() => throw new InvalidOperationException("Server returned 401 Unauthorized"));

        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = CreateReconnectingFactory(() => "ok");

        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var wrapper = new McpAuthRecoveryToolWrapper(
            innerFunction, Server, oauthHandler, clientFactory.Object, toolProvider, _logger.Object);

        // Act
        var result = await wrapper.InvokeAsync(new AIFunctionArguments());

        // Assert
        result?.ToString().ShouldBe("ok");
        clientFactory.Verify(x => x.GetOrCreateClientAsync(Server, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeCoreAsync_ClientInvalidationFails_StillRetries()
    {
        // Arrange - client invalidation throws, but retry should still happen
        var innerFunction = CreateMockFunction(() => throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden));

        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = CreateReconnectingFactory(() => "recovered");
        clientFactory.Setup(x => x.InvalidateClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalidation failed"));

        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var wrapper = new McpAuthRecoveryToolWrapper(
            innerFunction, Server, oauthHandler, clientFactory.Object, toolProvider, _logger.Object);

        // Act
        var result = await wrapper.InvokeAsync(new AIFunctionArguments());

        // Assert - should still recover despite invalidation failure
        result?.ToString().ShouldBe("recovered");
    }

    #endregion

    #region Wrap Static Method

    [Fact]
    public void Wrap_WithMixedTools_OnlyWrapsAIFunctions()
    {
        // Arrange
        var aiFunction = CreateMockFunction(() => "ok");
        var nonAiTool = new Mock<AITool>().Object;
        var tools = new List<AITool> { aiFunction, nonAiTool };

        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = new Mock<IMcpClientFactory>();
        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        // Act
        var wrapped = McpAuthRecoveryToolWrapper.Wrap(tools, Server, oauthHandler, clientFactory.Object, toolProvider);

        // Assert
        wrapped.Count.ShouldBe(2);
        wrapped[0].ShouldBeOfType<McpAuthRecoveryToolWrapper>();
        wrapped[1].ShouldNotBeOfType<McpAuthRecoveryToolWrapper>();
    }

    [Fact]
    public void Wrap_EmptyList_ReturnsEmptyList()
    {
        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = new Mock<IMcpClientFactory>();
        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var result = McpAuthRecoveryToolWrapper.Wrap(new List<AITool>(), Server, oauthHandler, clientFactory.Object, toolProvider);
        result.Count.ShouldBe(0);
    }

    [Fact]
    public void Wrap_NullList_ReturnsEmptyList()
    {
        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = new Mock<IMcpClientFactory>();
        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var result = McpAuthRecoveryToolWrapper.Wrap(null!, Server, oauthHandler, clientFactory.Object, toolProvider);
        result.Count.ShouldBe(0);
    }

    #endregion

    #region Helpers

    private static readonly McpServerConfig Server = new() { Name = "test-server" };

    /// <summary>A factory whose (re)connected client offers a fresh "test-tool" that runs <paramref name="onFreshCall"/>.</summary>
    private static Mock<IMcpClientFactory> CreateReconnectingFactory(Func<object?> onFreshCall)
    {
        var adapter = new Mock<IMcpClientAdapter>();
        adapter.Setup(a => a.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new List<AITool> { CreateMockFunction(onFreshCall) });
        var factory = new Mock<IMcpClientFactory>();
        factory.Setup(x => x.InvalidateClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        factory.Setup(x => x.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>())).ReturnsAsync(adapter.Object);
        return factory;
    }

    private McpAuthRecoveryToolWrapper CreateWrapper(AIFunction innerFunction)
    {
        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = new Mock<IMcpClientFactory>();
        clientFactory.Setup(x => x.InvalidateClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        return new McpAuthRecoveryToolWrapper(
            innerFunction, Server, oauthHandler, clientFactory.Object, toolProvider, _logger.Object);
    }

    private static AIFunction CreateMockFunction(Func<object?> action)
    {
        return AIFunctionFactory.Create((CancellationToken _) => action(),
            new AIFunctionFactoryOptions { Name = "test-tool", Description = "Test tool" });
    }

    private static McpToolProvider CreateMockToolProvider(IMcpClientFactory clientFactory)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AIOptions());
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
        var logger = new Mock<ILogger<McpToolProvider>>();

        return new McpToolProvider(new StaticOptionsMonitor<AIOptions>(options.Value), new OptionsBackedMcpServerCatalog(options), clientFactory, loggerFactory.Object, logger.Object);
    }

    private sealed class TrackingOAuthHandler : McpOAuthClientHandler
    {
        public TrackingOAuthHandler()
            : base(new StubHttpClientFactory(), NullLogger<McpOAuthClientHandler>.Instance)
        {
        }

        public int InvalidateCount { get; private set; }
        public List<string> InvalidatedServers { get; } = [];

        public override bool InvalidateToken(string serverName)
        {
            InvalidateCount++;
            InvalidatedServers.Add(serverName);
            return true;
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHttpMessageHandler());
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    #endregion
}
