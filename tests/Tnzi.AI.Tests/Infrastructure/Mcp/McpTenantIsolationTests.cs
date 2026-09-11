using System.Reflection;
using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

/// <summary>
/// MCP 运行时缓存的租户隔离测试。
/// </summary>
/// <remarks>
/// 多租户下 <c>McpServerRegistration</c> 的唯一索引是 (TenantId, Name)：两个租户可以注册同名 server
/// 而端点与凭据各不相同。<c>McpServerCatalog</c> 的 DB 快照已按租户分桶，但下游的连接缓存
/// （<see cref="McpClientFactory"/>）与工具缓存（<see cref="McpToolProvider"/>）曾只按 Name 分桶 ——
/// 租户 A 先跑会把用 A 凭据建立的连接与工具清单留在缓存里，TTL 内租户 B 直接命中，
/// 于是 B 的 agent 拿着 A 的凭据打到 A 的外部服务器。
/// </remarks>
public class McpTenantIsolationTests
{
    private static readonly string TenantA = "11111111-1111-1111-1111-111111111111";
    private static readonly string TenantB = "22222222-2222-2222-2222-222222222222";

    private readonly Mock<ILoggerFactory> _loggerFactory = new();
    private readonly Mock<ILogger<McpClientFactory>> _factoryLogger = new();
    private readonly Mock<ILogger<McpToolProvider>> _providerLogger = new();

    public McpTenantIsolationTests()
    {
        _loggerFactory
            .Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(NullLogger.Instance);
    }

    #region McpClientFactory - 连接缓存

    [Fact]
    public async Task GetOrCreateClient_SameServerNameDifferentTenants_DoesNotShareConnection()
    {
        var factory = new RecordingMcpClientFactory(_loggerFactory.Object, _factoryLogger.Object);

        var clientA = await factory.GetOrCreateClientAsync(TenantScopedConfig("github", TenantA, "token-a"));
        var clientB = await factory.GetOrCreateClientAsync(TenantScopedConfig("github", TenantB, "token-b"));

        // 两个租户各自建连，互不复用
        clientA.ShouldNotBeSameAs(clientB);
        factory.CreatedConfigs.Count.ShouldBe(2);
        factory.CreatedConfigs[0].Headers["Authorization"].ShouldBe("Bearer token-a");
        factory.CreatedConfigs[1].Headers["Authorization"].ShouldBe("Bearer token-b");
    }

    [Fact]
    public async Task GetOrCreateClient_SameServerSameTenant_ReusesConnection()
    {
        var factory = new RecordingMcpClientFactory(_loggerFactory.Object, _factoryLogger.Object);

        var first = await factory.GetOrCreateClientAsync(TenantScopedConfig("github", TenantA, "token-a"));
        var second = await factory.GetOrCreateClientAsync(TenantScopedConfig("github", TenantA, "token-a"));

        second.ShouldBeSameAs(first);
        factory.CreatedConfigs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetOrCreateClient_DeploymentConfigServer_StaysProcessWide()
    {
        // 部署配置条目没有租户维度（同一份凭据），键退化为名字，所有租户共用一条连接
        var factory = new RecordingMcpClientFactory(_loggerFactory.Object, _factoryLogger.Object);

        var first = await factory.GetOrCreateClientAsync(DeploymentConfig("shared"));
        var second = await factory.GetOrCreateClientAsync(DeploymentConfig("shared"));

        second.ShouldBeSameAs(first);
        factory.CreatedConfigs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task InvalidateClient_ByName_ClearsEveryTenantsConnection()
    {
        // 注册表 CRUD 只带得出名字（工厂是 Singleton，没有租户上下文）。
        // 按名字失效必须覆盖全部租户桶：多失效一个只是重连一次，漏失效一个是继续用作废的凭据。
        var factory = new RecordingMcpClientFactory(_loggerFactory.Object, _factoryLogger.Object);

        var clientA = (RecordingAdapter)await factory.GetOrCreateClientAsync(TenantScopedConfig("github", TenantA, "token-a"));
        var clientB = (RecordingAdapter)await factory.GetOrCreateClientAsync(TenantScopedConfig("github", TenantB, "token-b"));

        await factory.InvalidateClientAsync("github");

        clientA.Disposed.ShouldBeTrue();
        clientB.Disposed.ShouldBeTrue();
        GetCacheKeys(factory).ShouldBeEmpty();
    }

    [Fact]
    public async Task InvalidateClient_ByName_LeavesOtherServersAlone()
    {
        var factory = new RecordingMcpClientFactory(_loggerFactory.Object, _factoryLogger.Object);

        var github = (RecordingAdapter)await factory.GetOrCreateClientAsync(TenantScopedConfig("github", TenantA, "token-a"));
        var slack = (RecordingAdapter)await factory.GetOrCreateClientAsync(TenantScopedConfig("slack", TenantA, "token-s"));

        await factory.InvalidateClientAsync("github");

        github.Disposed.ShouldBeTrue();
        slack.Disposed.ShouldBeFalse();
        GetCacheKeys(factory).Count.ShouldBe(1);
    }

    #endregion

    #region McpToolProvider - 工具缓存

    [Fact]
    public async Task GetTools_SameServerNameDifferentTenants_DoesNotServeCachedToolList()
    {
        var catalog = new SwitchableCatalog(TenantScopedConfig("github", TenantA, "token-a"));
        var clientFactory = new Mock<IMcpClientFactory>();

        var adapterA = AdapterReturning("tool_a");
        var adapterB = AdapterReturning("tool_b");
        clientFactory
            .Setup(x => x.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((McpServerConfig config, CancellationToken _) =>
                config.TenantKey == TenantA ? adapterA : adapterB);

        var provider = new McpToolProvider(
            new StaticOptionsMonitor<AIOptions>(AiOptionsWithToolCache()),
            catalog,
            clientFactory.Object,
            _loggerFactory.Object,
            _providerLogger.Object);

        var toolsForA = await provider.GetToolsAsync();
        toolsForA.Select(t => t.Name).ShouldContain("tool_a");

        // 同名 server，换成租户 B 的配置（catalog 已经正确下发 B 的那一份）
        catalog.Current = TenantScopedConfig("github", TenantB, "token-b");
        var toolsForB = await provider.GetToolsAsync();

        toolsForB.Select(t => t.Name).ShouldContain("tool_b");
        toolsForB.Select(t => t.Name).ShouldNotContain("tool_a");
    }

    [Fact]
    public async Task GetTools_SameServerSameTenant_ServesCachedToolList()
    {
        var catalog = new SwitchableCatalog(TenantScopedConfig("github", TenantA, "token-a"));
        var clientFactory = new Mock<IMcpClientFactory>();

        var listCalls = 0;
        var adapter = new Mock<IMcpClientAdapter>();
        adapter.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                listCalls++;
                return new List<AITool> { AIFunctionFactory.Create(() => "ok", "tool_a") };
            });
        clientFactory
            .Setup(x => x.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adapter.Object);

        var provider = new McpToolProvider(
            new StaticOptionsMonitor<AIOptions>(AiOptionsWithToolCache()),
            catalog,
            clientFactory.Object,
            _loggerFactory.Object,
            _providerLogger.Object);

        await provider.GetToolsAsync();
        await provider.GetToolsAsync();

        listCalls.ShouldBe(1);
    }

    [Fact]
    public async Task InvalidateToolCache_ByName_ClearsEveryTenantsEntry()
    {
        var catalog = new SwitchableCatalog(TenantScopedConfig("github", TenantA, "token-a"));
        var clientFactory = new Mock<IMcpClientFactory>();

        var callsByTenant = new Dictionary<string, int> { [TenantA] = 0, [TenantB] = 0 };
        clientFactory
            .Setup(x => x.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((McpServerConfig config, CancellationToken _) =>
            {
                var tenant = config.TenantKey!;
                var adapter = new Mock<IMcpClientAdapter>();
                adapter.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() =>
                    {
                        callsByTenant[tenant]++;
                        return new List<AITool> { AIFunctionFactory.Create(() => "ok", "tool_" + tenant[..1]) };
                    });
                return adapter.Object;
            });

        var provider = new McpToolProvider(
            new StaticOptionsMonitor<AIOptions>(AiOptionsWithToolCache()),
            catalog,
            clientFactory.Object,
            _loggerFactory.Object,
            _providerLogger.Object);

        await provider.GetToolsAsync();
        catalog.Current = TenantScopedConfig("github", TenantB, "token-b");
        await provider.GetToolsAsync();
        callsByTenant[TenantA].ShouldBe(1);
        callsByTenant[TenantB].ShouldBe(1);

        provider.InvalidateCache("github");

        catalog.Current = TenantScopedConfig("github", TenantA, "token-a");
        await provider.GetToolsAsync();
        catalog.Current = TenantScopedConfig("github", TenantB, "token-b");
        await provider.GetToolsAsync();

        callsByTenant[TenantA].ShouldBe(2);
        callsByTenant[TenantB].ShouldBe(2);
    }

    #endregion

    #region 租户键来源

    [Fact]
    public void Mapper_StampsTenantKeyFromTheRowItself()
    {
        var tenantId = Guid.Parse(TenantA);
        var entity = new McpServerRegistration
        {
            Name = "github",
            ServerUrl = "https://mcp.example.com/sse",
            Transport = "sse",
            TenantId = tenantId
        };

        var config = McpServerRegistrationMapper.ToServerConfig(entity, _ => string.Empty);

        config.TenantKey.ShouldBe(tenantId.ToString());
    }

    [Fact]
    public void Mapper_SingleTenantRow_LeavesTenantKeyNull()
    {
        var entity = new McpServerRegistration
        {
            Name = "github",
            ServerUrl = "https://mcp.example.com/sse",
            Transport = "sse",
            TenantId = null
        };

        var config = McpServerRegistrationMapper.ToServerConfig(entity, _ => string.Empty);

        // 单租户部署：键退化为名字，与引入租户维度之前逐字相同
        config.TenantKey.ShouldBeNull();
        McpCacheKey.For(config).ShouldBe("github");
    }

    [Fact]
    public void CacheKey_SeparatesTenants_AndKeepsNameAddressable()
    {
        var a = McpCacheKey.For(TenantScopedConfig("github", TenantA, "token-a"));
        var b = McpCacheKey.For(TenantScopedConfig("github", TenantB, "token-b"));

        a.ShouldNotBe(b);
        McpCacheKey.MatchesServerName(a, "github").ShouldBeTrue();
        McpCacheKey.MatchesServerName(b, "GITHUB").ShouldBeTrue();
        McpCacheKey.MatchesServerName(a, "slack").ShouldBeFalse();
        McpCacheKey.ServerName(a).ShouldBe("github");
        McpCacheKey.ServerName("github").ShouldBe("github");
    }

    #endregion

    #region Helpers

    private static McpServerConfig TenantScopedConfig(string name, string tenantKey, string token) => new()
    {
        Name = name,
        TenantKey = tenantKey,
        ConnectionType = McpConnectionType.Http,
        Endpoint = $"https://{tenantKey}.example.com/mcp",
        Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
    };

    private static McpServerConfig DeploymentConfig(string name) => new()
    {
        Name = name,
        ConnectionType = McpConnectionType.Stdio,
        Command = "echo",
        Arguments = ["hello"]
    };

    private static AIOptions AiOptionsWithToolCache() => new()
    {
        Mcp = new McpOptions { Enabled = true, ToolCacheSeconds = 300, Servers = [] }
    };

    private static IMcpClientAdapter AdapterReturning(string toolName)
    {
        var adapter = new Mock<IMcpClientAdapter>();
        adapter.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AITool> { AIFunctionFactory.Create(() => "ok", toolName) });
        return adapter.Object;
    }

    private static IReadOnlyCollection<string> GetCacheKeys(McpClientFactory factory)
    {
        var cacheField = typeof(McpClientFactory)
            .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cache = (ConcurrentDictionary<string, IMcpClientAdapter>)cacheField.GetValue(factory)!;
        return cache.Keys.ToList();
    }

    /// <summary>每次建连返回一个新适配器，并记录建连时用的配置（凭据在 Headers 上）。</summary>
    private sealed class RecordingMcpClientFactory : McpClientFactory
    {
        public RecordingMcpClientFactory(ILoggerFactory loggerFactory, ILogger<McpClientFactory> logger)
            : base(loggerFactory, logger)
        {
        }

        public List<McpServerConfig> CreatedConfigs { get; } = [];

        protected internal override Task<IMcpClientAdapter> CreateAndConnectInternalAsync(McpServerConfig config, CancellationToken ct)
        {
            CreatedConfigs.Add(config);
            return Task.FromResult<IMcpClientAdapter>(new RecordingAdapter());
        }
    }

    private sealed class RecordingAdapter : IMcpClientAdapter
    {
        public bool Disposed { get; private set; }

        public Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AITool>>(Array.Empty<AITool>());

        public Task<bool> IsConnectedAsync(CancellationToken ct = default) => Task.FromResult(true);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>可切换当前有效服务器的 catalog 桩，用于模拟「同名 server 换了租户」。</summary>
    private sealed class SwitchableCatalog : IMcpServerCatalog
    {
        public SwitchableCatalog(McpServerConfig current)
        {
            Current = current;
        }

        public McpServerConfig Current { get; set; }

        public Task<IReadOnlyList<McpServerConfig>> GetEffectiveServersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<McpServerConfig>>(new List<McpServerConfig> { Current });

        public Task<McpServerConfig?> FindServerAsync(string serverName, CancellationToken ct = default) =>
            Task.FromResult<McpServerConfig?>(
                string.Equals(Current.Name, serverName, StringComparison.OrdinalIgnoreCase) ? Current : null);

        public void Invalidate()
        {
        }
    }

    #endregion
}
