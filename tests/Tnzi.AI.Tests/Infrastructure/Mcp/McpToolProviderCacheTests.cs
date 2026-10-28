using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

/// <summary>
/// McpToolProvider 缓存失效测试
/// </summary>
public class McpToolProviderCacheTests
{
    private readonly Mock<IOptions<AIOptions>> _options = new();
    private readonly Mock<IMcpClientFactory> _clientFactory = new();
    private readonly Mock<ILoggerFactory> _loggerFactory = new();
    private readonly Mock<ILogger<McpToolProvider>> _logger = new();

    public McpToolProviderCacheTests()
    {
        _loggerFactory
            .Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(NullLogger.Instance);
    }

    [Fact]
    public async Task InvalidateCache_SpecificServer_OnlyClearsThatServerCache()
    {
        // Arrange
        var (provider, mockAdapter1, mockAdapter2) = SetupTwoServers();
        var callCount1 = 0;
        var callCount2 = 0;
        mockAdapter1.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount1++;
                return new List<AITool>();
            });
        mockAdapter2.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount2++;
                return new List<AITool>();
            });

        // 初次调用填充缓存
        await provider.GetToolsAsync();
        callCount1.ShouldBe(1);
        callCount2.ShouldBe(1);

        // 再次调用走缓存
        await provider.GetToolsAsync();
        callCount1.ShouldBe(1);
        callCount2.ShouldBe(1);

        // Act: 只失效 server1
        provider.InvalidateCache("server1");

        // 再次调用：server1 应重新拉取，server2 仍走缓存
        await provider.GetToolsAsync();
        callCount1.ShouldBe(2);
        callCount2.ShouldBe(1);
    }

    [Fact]
    public async Task InvalidateCache_AllServers_ClearsAllCache()
    {
        // Arrange
        var (provider, mockAdapter1, mockAdapter2) = SetupTwoServers();
        var callCount1 = 0;
        var callCount2 = 0;
        mockAdapter1.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount1++;
                return new List<AITool>();
            });
        mockAdapter2.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount2++;
                return new List<AITool>();
            });

        // 初次调用填充缓存
        await provider.GetToolsAsync();
        callCount1.ShouldBe(1);
        callCount2.ShouldBe(1);

        // Act: 失效全部
        provider.InvalidateCache(null);

        // 再次调用：全部重新拉取
        await provider.GetToolsAsync();
        callCount1.ShouldBe(2);
        callCount2.ShouldBe(2);
    }

    [Fact]
    public async Task InvalidateCache_NonExistentServer_DoesNotThrow()
    {
        // Arrange
        var provider = CreateSimpleProvider();

        // Act & Assert: should not throw
        provider.InvalidateCache("non_existent_server");
    }

    [Fact]
    public async Task GetServerToolNames_ReturnsCorrectMapping()
    {
        // Arrange
        var (provider, mockAdapter1, _) = SetupTwoServers();
        var tool1 = AIFunctionFactory.Create(() => "hello", "tool_from_server1");
        var tool2 = AIFunctionFactory.Create(() => "world", "another_tool_server1");
        mockAdapter1.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AITool> { tool1, tool2 });

        // Act: populate cache
        await provider.GetToolsAsync();

        // Assert
        var mapping = provider.GetServerToolNames("server1");
        mapping.ShouldNotBeNull();
        mapping.ShouldContain("tool_from_server1");
        mapping.ShouldContain("another_tool_server1");
    }

    [Fact]
    public async Task GetServerToolNames_UnknownServer_ReturnsEmpty()
    {
        // Arrange
        var provider = CreateSimpleProvider();

        // Act
        var mapping = provider.GetServerToolNames("unknown");

        // Assert
        mapping.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetToolsAsync_PermissionEvaluatorWithoutCurrentRules_StillWrapsTools()
    {
        var server = new McpServerConfig { Name = "server1" };
        var aiOptions = CreateAiOptions(server);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var tool = AIFunctionFactory.Create(() => "hello", "tool_from_server1");
        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AITool> { tool });
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "server1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        var monitor = new MutableOptionsMonitor<AIOptions>(new AIOptions
        {
            Permissions = new ToolPermissionOptions
            {
                Enabled = false
            }
        });
        using var evaluator = new ConfiguredToolPermissionEvaluator(monitor);

        var provider = new McpToolProvider(
            new StaticOptionsMonitor<AIOptions>(_options.Object.Value),
            new OptionsBackedMcpServerCatalog(_options.Object),
            _clientFactory.Object,
            _loggerFactory.Object,
            _logger.Object,
            permissionEvaluator: evaluator);

        var result = await provider.GetToolsAsync();

        result.Count.ShouldBe(1);
        result[0].ShouldBeOfType<McpAuditToolWrapper>();

        monitor.Set(new AIOptions
        {
            Permissions = new ToolPermissionOptions
            {
                Enabled = true,
                SessionRules =
                [
                    new ToolPermissionRuleOptions
                    {
                        ToolPattern = "tool_from_server1",
                        Behavior = PermissionBehavior.Deny,
                        Reason = "Blocked after reload"
                    }
                ]
            }
        });

        var invocationResult = await ((AIFunction)result[0]).InvokeAsync(new AIFunctionArguments());
        invocationResult.ShouldBe("Tool call rejected: Blocked after reload");
    }

    /// <summary>
    /// 服务器要求审批而应用没注册审批处理器（也没有权限评估器）时，工具仍要被审批包装，调用被拒绝；
    /// 不包装就等于让这台服务器上需要审批的工具直接执行。
    /// </summary>
    [Fact]
    public async Task GetToolsAsync_ServerRequiresApproval_NoHandlerNoEvaluator_ToolCallIsRejected()
    {
        var server = new McpServerConfig { Name = "server1", ApprovalMode = McpApprovalMode.AlwaysRequire };
        var aiOptions = CreateAiOptions(server);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var executed = false;
        var tool = AIFunctionFactory.Create(() => { executed = true; return "hello"; }, "tool_from_server1");
        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AITool> { tool });
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "server1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        var provider = new McpToolProvider(
            new StaticOptionsMonitor<AIOptions>(_options.Object.Value),
            new OptionsBackedMcpServerCatalog(_options.Object),
            _clientFactory.Object,
            _loggerFactory.Object,
            _logger.Object);

        var result = await provider.GetToolsAsync();

        var invocationResult = await ((AIFunction)result.ShouldHaveSingleItem()).InvokeAsync(new AIFunctionArguments());
        invocationResult.ShouldBe("Tool call rejected: approval handler is not configured.");
        executed.ShouldBeFalse();
    }

    /// <summary>
    /// 缓存的工具绑在列出它们的那条连接上。子进程崩溃或 HTTP 会话丢失后工厂会重连（30 秒一次健康检查），
    /// 但此前命中缓存时根本不问工厂，绑在死连接上的工具一直发到 ToolCacheSeconds（默认 300 秒）过期。
    /// </summary>
    [Fact]
    public async Task GetToolsAsync_FactoryReconnected_ListsAgainFromTheNewConnection()
    {
        // Approval is not what this covers: with the default (AlwaysRequire) and no handler the call is refused.
        var server = new McpServerConfig { Name = "server1", ApprovalMode = McpApprovalMode.NeverRequire };
        _options.Setup(x => x.Value).Returns(CreateAiOptions(server));

        var oldConnection = new Mock<IMcpClientAdapter>();
        oldConnection.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AITool> { AIFunctionFactory.Create(() => "old", "search") });
        var newConnection = new Mock<IMcpClientAdapter>();
        var newListings = 0;
        newConnection.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                newListings++;
                return new List<AITool> { AIFunctionFactory.Create(() => "new", "search") };
            });

        var current = oldConnection.Object;
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => current);
        var provider = new McpToolProvider(new StaticOptionsMonitor<AIOptions>(_options.Object.Value), new OptionsBackedMcpServerCatalog(_options.Object), _clientFactory.Object, _loggerFactory.Object, _logger.Object);

        await provider.GetToolsAsync();
        (await provider.GetToolsAsync()).ShouldHaveSingleItem();
        oldConnection.Verify(x => x.ListToolsAsync(It.IsAny<CancellationToken>()), Times.Once);

        // the factory reconnected (old client disposed): the cached tools are bound to the dead client
        current = newConnection.Object;
        var tools = await provider.GetToolsAsync();

        newListings.ShouldBe(1);
        (await ((AIFunction)tools.ShouldHaveSingleItem()).InvokeAsync(new AIFunctionArguments()))?.ToString().ShouldBe("new");

        // and the new listing is cached again
        await provider.GetToolsAsync();
        newListings.ShouldBe(1);
    }

    private McpToolProvider CreateSimpleProvider()
    {
        var aiOptions = new AIOptions
        {
            Mcp = new McpOptions { Enabled = true, ToolCacheSeconds = 300, Servers = [] }
        };
        _options.Setup(x => x.Value).Returns(aiOptions);
        return new McpToolProvider(new StaticOptionsMonitor<AIOptions>(_options.Object.Value), new OptionsBackedMcpServerCatalog(_options.Object), _clientFactory.Object, _loggerFactory.Object, _logger.Object);
    }

    private (McpToolProvider provider, Mock<IMcpClientAdapter> adapter1, Mock<IMcpClientAdapter> adapter2) SetupTwoServers()
    {
        var server1 = new McpServerConfig { Name = "server1" };
        var server2 = new McpServerConfig { Name = "server2" };
        var aiOptions = CreateAiOptions(server1, server2);
        _options.Setup(x => x.Value).Returns(aiOptions);

        var mockAdapter1 = new Mock<IMcpClientAdapter>();
        var mockAdapter2 = new Mock<IMcpClientAdapter>();
        mockAdapter1.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AITool>());
        mockAdapter2.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AITool>());

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "server1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter1.Object);
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "server2"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter2.Object);

        var provider = new McpToolProvider(new StaticOptionsMonitor<AIOptions>(_options.Object.Value), new OptionsBackedMcpServerCatalog(_options.Object), _clientFactory.Object, _loggerFactory.Object, _logger.Object);
        return (provider, mockAdapter1, mockAdapter2);
    }

    private static AIOptions CreateAiOptions(params McpServerConfig[] servers)
    {
        return new AIOptions
        {
            Mcp = new McpOptions
            {
                Enabled = true,
                ToolCacheSeconds = 300,
                Servers = servers.ToList()
            }
        };
    }
}

file sealed class MutableOptionsMonitor<T> : IOptionsMonitor<T>
{
    private readonly List<Action<T, string?>> _listeners = [];

    public MutableOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; private set; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return new CallbackDisposable(() => _listeners.Remove(listener));
    }

    public void Set(T value, string? name = null)
    {
        CurrentValue = value;
        foreach (var listener in _listeners.ToArray())
        {
            listener(value, name);
        }
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }
}
