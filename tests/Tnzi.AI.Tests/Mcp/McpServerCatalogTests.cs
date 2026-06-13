using Microsoft.AspNetCore.DataProtection;
using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// McpServerCatalog 单元测试 — DB 注册表物化、与部署配置合并（同名 DB 优先）、
/// 总开关/禁用条目/legacy stdio 行/解密失败的过滤行为、快照 TTL 缓存与主动失效
/// </summary>
public class McpServerCatalogTests
{
    private readonly Mock<IRepository<McpServerRegistration, Guid>> _repository = new();
    private readonly StubDataProtectionProvider _dataProtection = new();
    private readonly Mock<IOptionsMonitor<AIOptions>> _optionsMonitor = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public McpServerCatalogTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRepository<McpServerRegistration, Guid>>(_ => _repository.Object);
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private McpServerCatalog CreateCatalog() => new(
        _optionsMonitor.Object,
        _scopeFactory,
        _dataProtection,
        NullLogger<McpServerCatalog>.Instance);

    private void SetupOptions(bool enabled = true, params McpServerConfig[] servers)
    {
        _optionsMonitor.Setup(m => m.CurrentValue).Returns(new AIOptions
        {
            Mcp = new McpOptions
            {
                Enabled = enabled,
                Servers = servers.ToList()
            }
        });
    }

    private void SetupDbRows(params McpServerRegistration[] rows)
    {
        var mock = rows.ToList().BuildMock();
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
    }

    private string FakeCipher(string plaintext) =>
        _dataProtection.CreateProtector(McpServerRegistration.AuthTokenProtectorPurpose).Protect(plaintext);

    private static McpServerRegistration MakeRegistration(
        string name,
        string transport = "sse",
        string url = "https://mcp.example.com/sse",
        bool isEnabled = true,
        string? authTokenEncrypted = null,
        string? authType = "bearer",
        int priority = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ServerUrl = url,
        Transport = transport,
        AuthType = authType,
        AuthTokenEncrypted = authTokenEncrypted,
        Priority = priority,
        IsEnabled = isEnabled
    };

    // =====================================================================
    // 总开关与来源合并
    // =====================================================================

    [Fact]
    public async Task GetEffectiveServersAsync_McpDisabled_ReturnsEmpty_EvenWithDbRows()
    {
        SetupOptions(enabled: false);
        SetupDbRows(MakeRegistration("db-server"));
        var catalog = CreateCatalog();

        var servers = await catalog.GetEffectiveServersAsync();

        servers.ShouldBeEmpty();
        // 总开关关闭时不应触碰数据库
        _repository.Verify(r => r.AsQueryable(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task GetEffectiveServersAsync_NoDbRows_ReturnsConfigServers()
    {
        SetupOptions(enabled: true, new McpServerConfig { Name = "cfg", Endpoint = "https://cfg.example.com" });
        SetupDbRows();
        var catalog = CreateCatalog();

        var servers = await catalog.GetEffectiveServersAsync();

        servers.Count.ShouldBe(1);
        servers[0].Name.ShouldBe("cfg");
    }

    [Fact]
    public async Task GetEffectiveServersAsync_DbRow_MaterializedAsHttpConfig_WithDecryptedBearerHeader()
    {
        SetupOptions(enabled: true);
        SetupDbRows(MakeRegistration("db-bearer", transport: "streamable-http",
            url: "https://db.example.com/mcp", authTokenEncrypted: FakeCipher("tok-123"), authType: "bearer"));
        var catalog = CreateCatalog();

        var servers = await catalog.GetEffectiveServersAsync();

        servers.Count.ShouldBe(1);
        var config = servers[0];
        config.Name.ShouldBe("db-bearer");
        config.ConnectionType.ShouldBe(McpConnectionType.Http);
        config.Endpoint.ShouldBe("https://db.example.com/mcp");
        config.Headers["Authorization"].ShouldBe("Bearer tok-123");
    }

    [Fact]
    public async Task GetEffectiveServersAsync_ApiKeyAuthType_MapsToApiKeyHeader()
    {
        SetupOptions(enabled: true);
        SetupDbRows(MakeRegistration("db-apikey", authTokenEncrypted: FakeCipher("key-456"), authType: "api-key"));
        var catalog = CreateCatalog();

        var servers = await catalog.GetEffectiveServersAsync();

        servers.Count.ShouldBe(1);
        servers[0].Headers["X-Api-Key"].ShouldBe("key-456");
        servers[0].Headers.ShouldNotContainKey("Authorization");
    }

    [Fact]
    public async Task GetEffectiveServersAsync_SameName_DbEntryOverridesConfigServer()
    {
        SetupOptions(enabled: true,
            new McpServerConfig { Name = "shared", Endpoint = "https://config.example.com" },
            new McpServerConfig { Name = "cfg-only", Endpoint = "https://cfg-only.example.com" });
        SetupDbRows(MakeRegistration("shared", url: "https://db-wins.example.com", authType: "none"));
        var catalog = CreateCatalog();

        var servers = await catalog.GetEffectiveServersAsync();

        servers.Count.ShouldBe(2);
        // DB 条目在前且覆盖同名部署配置
        servers[0].Name.ShouldBe("shared");
        servers[0].Endpoint.ShouldBe("https://db-wins.example.com");
        servers[1].Name.ShouldBe("cfg-only");
    }

    [Fact]
    public async Task GetEffectiveServersAsync_DbRows_OrderedByPriorityDescending()
    {
        SetupOptions(enabled: true);
        SetupDbRows(
            MakeRegistration("low", priority: 1, authType: "none"),
            MakeRegistration("high", priority: 10, authType: "none"));
        var catalog = CreateCatalog();

        var servers = await catalog.GetEffectiveServersAsync();

        servers.Select(s => s.Name).ShouldBe(["high", "low"]);
    }

    // =====================================================================
    // 过滤行为：禁用 / legacy stdio / 解密失败 / DB 故障
    // =====================================================================

    [Fact]
    public async Task GetEffectiveServersAsync_DisabledDbRow_IsNotMaterialized()
    {
        SetupOptions(enabled: true);
        SetupDbRows(
            MakeRegistration("enabled-one", authType: "none"),
            MakeRegistration("disabled-one", isEnabled: false, authType: "none"));
        var catalog = CreateCatalog();

        var servers = await catalog.GetEffectiveServersAsync();

        servers.Select(s => s.Name).ShouldBe(["enabled-one"]);
    }

    [Fact]
    public async Task GetEffectiveServersAsync_LegacyStdioRow_IsSkipped()
    {
        SetupOptions(enabled: true);
        SetupDbRows(
            MakeRegistration("legacy-stdio", transport: "stdio", authType: "none"),
            MakeRegistration("good-sse", authType: "none"));
        var catalog = CreateCatalog();

        var servers = await catalog.GetEffectiveServersAsync();

        servers.Select(s => s.Name).ShouldBe(["good-sse"]);
    }

    [Fact]
    public async Task GetEffectiveServersAsync_DecryptFailure_EntrySkipped_OthersSurvive()
    {
        SetupOptions(enabled: true);
        SetupDbRows(
            MakeRegistration("corrupt", authTokenEncrypted: "NOT_A_VALID_CIPHER", authType: "bearer"),
            MakeRegistration("healthy", authTokenEncrypted: FakeCipher("ok"), authType: "bearer"));
        var catalog = CreateCatalog();

        var servers = await catalog.GetEffectiveServersAsync();

        servers.Select(s => s.Name).ShouldBe(["healthy"]);
    }

    [Fact]
    public async Task GetEffectiveServersAsync_RepositoryThrows_FallsBackToConfigServers()
    {
        SetupOptions(enabled: true, new McpServerConfig { Name = "cfg", Endpoint = "https://cfg.example.com" });
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Throws(new InvalidOperationException("db down"));
        var catalog = CreateCatalog();

        var servers = await catalog.GetEffectiveServersAsync();

        servers.Count.ShouldBe(1);
        servers[0].Name.ShouldBe("cfg");
    }

    // =====================================================================
    // 快照缓存：TTL 命中 + 主动失效
    // =====================================================================

    [Fact]
    public async Task GetEffectiveServersAsync_SecondCallWithinTtl_DoesNotHitRepositoryAgain()
    {
        SetupOptions(enabled: true);
        SetupDbRows(MakeRegistration("cached", authType: "none"));
        var catalog = CreateCatalog();

        await catalog.GetEffectiveServersAsync();
        await catalog.GetEffectiveServersAsync();

        _repository.Verify(r => r.AsQueryable(It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task Invalidate_ForcesReloadOnNextCall()
    {
        SetupOptions(enabled: true);
        SetupDbRows(MakeRegistration("v1", authType: "none"));
        var catalog = CreateCatalog();

        var first = await catalog.GetEffectiveServersAsync();
        first.Select(s => s.Name).ShouldBe(["v1"]);

        // 模拟 admin 改名后 CRUD 调用 Invalidate
        SetupDbRows(MakeRegistration("v2", authType: "none"));
        catalog.Invalidate();

        var second = await catalog.GetEffectiveServersAsync();
        second.Select(s => s.Name).ShouldBe(["v2"]);
        _repository.Verify(r => r.AsQueryable(It.IsAny<bool>()), Times.Exactly(2));
    }

    // =====================================================================
    // FindServerAsync
    // =====================================================================

    [Fact]
    public async Task FindServerAsync_MatchesCaseInsensitively_AcrossBothSources()
    {
        SetupOptions(enabled: true, new McpServerConfig { Name = "CfgServer", Endpoint = "https://cfg.example.com" });
        SetupDbRows(MakeRegistration("DbServer", authType: "none"));
        var catalog = CreateCatalog();

        (await catalog.FindServerAsync("dbserver")).ShouldNotBeNull();
        (await catalog.FindServerAsync("CFGSERVER")).ShouldNotBeNull();
        (await catalog.FindServerAsync("missing")).ShouldBeNull();
    }

    /// <summary>
    /// Stub IDataProtectionProvider — marker-prefix 包装明文，无需真实 DataProtection key 基础设施。
    /// </summary>
    private sealed class StubDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new StubProtector();

        private sealed class StubProtector : IDataProtector
        {
            private const string Marker = "PROTECT::";
            public IDataProtector CreateProtector(string purpose) => this;
            public byte[] Protect(byte[] plaintext) => System.Text.Encoding.UTF8.GetBytes(Marker + System.Text.Encoding.UTF8.GetString(plaintext));
            public byte[] Unprotect(byte[] protectedData)
            {
                var s = System.Text.Encoding.UTF8.GetString(protectedData);
                if (!s.StartsWith(Marker, StringComparison.Ordinal))
                    throw new System.Security.Cryptography.CryptographicException("Invalid ciphertext");
                return System.Text.Encoding.UTF8.GetBytes(s.Substring(Marker.Length));
            }
        }
    }
}
