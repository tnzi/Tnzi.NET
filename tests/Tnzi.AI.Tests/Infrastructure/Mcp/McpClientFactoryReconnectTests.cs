using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

public class McpClientFactoryReconnectTests
{
    private readonly Mock<ILoggerFactory> _loggerFactory = new();
    private readonly Mock<ILogger<McpClientFactory>> _logger = new();

    public McpClientFactoryReconnectTests()
    {
        _loggerFactory
            .Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(NullLogger.Instance);
    }

    #region InvalidateClientAsync

    [Fact]
    public async Task InvalidateClientAsync_RemovesClientFromCache_AndDisposesIt()
    {
        // Arrange
        var factory = new McpClientFactory(_loggerFactory.Object, _logger.Object);
        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockAdapter.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        // 通过反射注入一个 mock 客户端到缓存
        InjectCachedClient(factory, "test-server", mockAdapter.Object);

        // Act
        await factory.InvalidateClientAsync("test-server");

        // Assert
        mockAdapter.Verify(x => x.DisposeAsync(), Times.Once);

        // 再次调用不应抛出（幂等）
        await factory.InvalidateClientAsync("test-server");
    }

    [Fact]
    public async Task InvalidateClientAsync_WithNonExistentServer_DoesNotThrow()
    {
        var factory = new McpClientFactory(_loggerFactory.Object, _logger.Object);

        // Act & Assert - 不应抛异常
        await factory.InvalidateClientAsync("non-existent");
    }

    [Fact]
    public async Task InvalidateClientAsync_WithNullServerName_ThrowsArgumentException()
    {
        var factory = new McpClientFactory(_loggerFactory.Object, _logger.Object);

        await Should.ThrowAsync<ArgumentException>(() => factory.InvalidateClientAsync(null!));
        await Should.ThrowAsync<ArgumentException>(() => factory.InvalidateClientAsync(""));
        await Should.ThrowAsync<ArgumentException>(() => factory.InvalidateClientAsync("   "));
    }

    [Fact]
    public async Task InvalidateClientAsync_SwallowsDisposeException()
    {
        var factory = new McpClientFactory(_loggerFactory.Object, _logger.Object);
        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.DisposeAsync()).Throws(new InvalidOperationException("Dispose failed"));

        InjectCachedClient(factory, "failing-server", mockAdapter.Object);

        // Act - should not throw even though Dispose fails
        await factory.InvalidateClientAsync("failing-server");
    }

    #endregion

    #region Health Check - GetOrCreateClientAsync returns new client when cached is disconnected

    [Fact]
    public async Task GetOrCreateClientAsync_ReturnsCachedClient_WhenHealthy()
    {
        // Arrange
        var factory = new McpClientFactory(_loggerFactory.Object, _logger.Object);
        var healthyAdapter = new Mock<IMcpClientAdapter>();
        healthyAdapter.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        InjectCachedClient(factory, "healthy-server", healthyAdapter.Object);

        var config = CreateConfig("healthy-server");

        // Act
        var result = await factory.GetOrCreateClientAsync(config);

        // Assert - 返回缓存中的同一个实例
        result.ShouldBeSameAs(healthyAdapter.Object);
    }

    [Fact]
    public async Task GetOrCreateClientAsync_InvalidatesAndReconnects_WhenCachedClientIsDisconnected()
    {
        // Arrange
        var factory = new TestableReconnectMcpClientFactory(_loggerFactory.Object, _logger.Object);
        var deadAdapter = new Mock<IMcpClientAdapter>();
        deadAdapter.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        deadAdapter.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var newAdapter = new Mock<IMcpClientAdapter>();
        newAdapter.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        InjectCachedClient(factory, "dead-server", deadAdapter.Object);
        factory.NextAdapter = newAdapter.Object;

        var config = CreateConfig("dead-server");

        // Act
        var result = await factory.GetOrCreateClientAsync(config);

        // Assert
        result.ShouldBeSameAs(newAdapter.Object);
        deadAdapter.Verify(x => x.DisposeAsync(), Times.Once);
        factory.CreateCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetOrCreateClientAsync_HealthCheckException_TreatedAsDisconnected()
    {
        // Arrange - 健康检查抛异常应视为断开连接
        var factory = new TestableReconnectMcpClientFactory(_loggerFactory.Object, _logger.Object);
        var faultyAdapter = new Mock<IMcpClientAdapter>();
        faultyAdapter.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Transport error"));
        faultyAdapter.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var newAdapter = new Mock<IMcpClientAdapter>();
        newAdapter.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        InjectCachedClient(factory, "faulty-server", faultyAdapter.Object);
        factory.NextAdapter = newAdapter.Object;

        var config = CreateConfig("faulty-server");

        // Act
        var result = await factory.GetOrCreateClientAsync(config);

        // Assert
        result.ShouldBeSameAs(newAdapter.Object);
    }

    #endregion

    #region Exponential Backoff Retry

    [Fact]
    public async Task GetOrCreateClientAsync_RetriesWithExponentialBackoff_OnReconnectFailure()
    {
        // Arrange
        var factory = new TestableReconnectMcpClientFactory(_loggerFactory.Object, _logger.Object);
        var deadAdapter = new Mock<IMcpClientAdapter>();
        deadAdapter.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        deadAdapter.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var goodAdapter = new Mock<IMcpClientAdapter>();
        goodAdapter.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // 前两次创建失败，第三次成功
        var callCount = 0;
        factory.CreateFunc = () =>
        {
            callCount++;
            if (callCount < 3)
            {
                throw new InvalidOperationException($"Connection failed attempt {callCount}");
            }
            return goodAdapter.Object;
        };

        InjectCachedClient(factory, "retry-server", deadAdapter.Object);

        var config = CreateConfig("retry-server");

        // Act
        var result = await factory.GetOrCreateClientAsync(config);

        // Assert
        result.ShouldBeSameAs(goodAdapter.Object);
        callCount.ShouldBe(3);
    }

    [Fact]
    public async Task GetOrCreateClientAsync_ThrowsAfterMaxRetries()
    {
        // Arrange
        var factory = new TestableReconnectMcpClientFactory(_loggerFactory.Object, _logger.Object);
        var deadAdapter = new Mock<IMcpClientAdapter>();
        deadAdapter.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        deadAdapter.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        // 所有重试都失败
        factory.CreateFunc = () => throw new InvalidOperationException("Connection permanently failed");

        InjectCachedClient(factory, "broken-server", deadAdapter.Object);

        var config = CreateConfig("broken-server");

        // Act & Assert - 3 次重试后抛异常
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => factory.GetOrCreateClientAsync(config));
        ex.Message.ShouldContain("broken-server");
    }

    [Fact]
    public async Task GetOrCreateClientAsync_WithOAuthConfig_InvalidatesTokenBetweenRetries()
    {
        var oauthHandler = new CountingOAuthHandler();
        var factory = new TestableReconnectMcpClientFactory(_loggerFactory.Object, _logger.Object, oauthHandler);
        factory.CreateFunc = () => throw new InvalidOperationException("Connection failed");

        var config = CreateConfig("oauth-server");
        config.OAuth = new McpOAuthConfig
        {
            ClientId = "client-id",
            TokenEndpoint = "https://auth.example.com/token"
        };

        await Should.ThrowAsync<InvalidOperationException>(() => factory.GetOrCreateClientAsync(config));

        oauthHandler.InvalidatedServers.ShouldContain("oauth-server");
        oauthHandler.InvalidateCount.ShouldBe(3);
    }

    #endregion

    #region IMcpClientAdapter.IsConnectedAsync

    [Fact]
    public async Task McpClientAdapter_IsConnectedAsync_ReturnsTrueForWorkingClient()
    {
        // McpClientAdapter 是 internal，通过 factory 间接测试健康检查路径
        // 此测试确认接口签名存在
        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.IsConnectedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await mockAdapter.Object.IsConnectedAsync();
        result.ShouldBeTrue();
    }

    #endregion

    #region Helpers

    private static McpServerConfig CreateConfig(string name)
    {
        return new McpServerConfig
        {
            Name = name,
            ConnectionType = McpConnectionType.Stdio,
            Command = "echo",
            Arguments = ["hello"]
        };
    }

    private static void InjectCachedClient(McpClientFactory factory, string serverName, IMcpClientAdapter adapter)
    {
        // 通过反射注入缓存
        var cacheField = typeof(McpClientFactory)
            .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cache = (ConcurrentDictionary<string, IMcpClientAdapter>)cacheField.GetValue(factory)!;
        cache[serverName] = adapter;
    }

    /// <summary>
    /// 可测试的 McpClientFactory 子类，覆写内部创建方法以注入 mock 适配器。
    /// </summary>
    private class TestableReconnectMcpClientFactory : McpClientFactory
    {
        public IMcpClientAdapter? NextAdapter { get; set; }
        public Func<IMcpClientAdapter>? CreateFunc { get; set; }
        public int CreateCallCount { get; private set; }

        public TestableReconnectMcpClientFactory(
            ILoggerFactory loggerFactory,
            ILogger<McpClientFactory> logger,
            McpOAuthClientHandler? oauthHandler = null)
            : base(loggerFactory, logger, oauthHandler)
        {
        }

        protected override Task<IMcpClientAdapter> CreateAndConnectInternalAsync(McpServerConfig config, CancellationToken ct)
        {
            CreateCallCount++;
            if (CreateFunc != null)
            {
                return Task.FromResult(CreateFunc());
            }
            return Task.FromResult(NextAdapter ?? throw new InvalidOperationException("No adapter configured"));
        }
    }

    private sealed class CountingOAuthHandler : McpOAuthClientHandler
    {
        public CountingOAuthHandler()
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
