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
        var callCount = 0;
        var innerFunction = CreateMockFunction(() =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
            }
            return "retry-success";
        });

        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = new Mock<IMcpClientFactory>();
        clientFactory.Setup(x => x.InvalidateClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var wrapper = new McpAuthRecoveryToolWrapper(
            innerFunction, "test-server", oauthHandler, clientFactory.Object, toolProvider, _logger.Object);

        // Act
        var result = await wrapper.InvokeAsync(new AIFunctionArguments());

        // Assert
        result?.ToString().ShouldBe("retry-success");
        callCount.ShouldBe(2);
        oauthHandler.InvalidateCount.ShouldBe(1);
        oauthHandler.InvalidatedServers.ShouldContain("test-server");
        clientFactory.Verify(x => x.InvalidateClientAsync("test-server", It.IsAny<CancellationToken>()), Times.Once);
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
        var clientFactory = new Mock<IMcpClientFactory>();
        clientFactory.Setup(x => x.InvalidateClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var wrapper = new McpAuthRecoveryToolWrapper(
            innerFunction, "test-server", oauthHandler, clientFactory.Object, toolProvider, _logger.Object);

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
            innerFunction, "test-server", oauthHandler, clientFactory.Object, toolProvider, _logger.Object);

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
        var callCount = 0;
        var innerFunction = CreateMockFunction(() =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new InvalidOperationException("Server returned 401 Unauthorized");
            }
            return "ok";
        });

        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = new Mock<IMcpClientFactory>();
        clientFactory.Setup(x => x.InvalidateClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var wrapper = new McpAuthRecoveryToolWrapper(
            innerFunction, "test-server", oauthHandler, clientFactory.Object, toolProvider, _logger.Object);

        // Act
        var result = await wrapper.InvokeAsync(new AIFunctionArguments());

        // Assert
        result?.ToString().ShouldBe("ok");
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task InvokeCoreAsync_ClientInvalidationFails_StillRetries()
    {
        // Arrange - client invalidation throws, but retry should still happen
        var callCount = 0;
        var innerFunction = CreateMockFunction(() =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden);
            }
            return "recovered";
        });

        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = new Mock<IMcpClientFactory>();
        clientFactory.Setup(x => x.InvalidateClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalidation failed"));

        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var wrapper = new McpAuthRecoveryToolWrapper(
            innerFunction, "test-server", oauthHandler, clientFactory.Object, toolProvider, _logger.Object);

        // Act
        var result = await wrapper.InvokeAsync(new AIFunctionArguments());

        // Assert - should still recover despite invalidation failure
        result?.ToString().ShouldBe("recovered");
        callCount.ShouldBe(2);
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
        var wrapped = McpAuthRecoveryToolWrapper.Wrap(tools, "server", oauthHandler, clientFactory.Object, toolProvider);

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

        var result = McpAuthRecoveryToolWrapper.Wrap(new List<AITool>(), "server", oauthHandler, clientFactory.Object, toolProvider);
        result.Count.ShouldBe(0);
    }

    [Fact]
    public void Wrap_NullList_ReturnsEmptyList()
    {
        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = new Mock<IMcpClientFactory>();
        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        var result = McpAuthRecoveryToolWrapper.Wrap(null!, "server", oauthHandler, clientFactory.Object, toolProvider);
        result.Count.ShouldBe(0);
    }

    #endregion

    #region Helpers

    private McpAuthRecoveryToolWrapper CreateWrapper(AIFunction innerFunction)
    {
        var oauthHandler = new TrackingOAuthHandler();
        var clientFactory = new Mock<IMcpClientFactory>();
        clientFactory.Setup(x => x.InvalidateClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolProvider = CreateMockToolProvider(clientFactory.Object);

        return new McpAuthRecoveryToolWrapper(
            innerFunction, "test-server", oauthHandler, clientFactory.Object, toolProvider, _logger.Object);
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
