using Microsoft.AspNetCore.DataProtection;
using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// McpServerRegistryService 单元测试 — 覆盖 CRUD、加密、HasAuthToken 暴露语义、
/// transport/URL 校验（stdio 拒绝）、真实 TestConnection 探针与运行时缓存失效联动
/// </summary>
public class McpServerRegistryServiceTests
{
    private readonly Mock<IRepository<McpServerRegistration, Guid>> _repository = new();
    private readonly StubDataProtectionProvider _dataProtection = new();
    private readonly Mock<IMcpClientFactory> _clientFactory = new();
    private readonly Mock<IMcpServerCatalog> _catalog = new();
    private readonly Mock<IMcpToolProvider> _toolProvider = new();
    private readonly IServiceProvider _serviceProvider;

    public McpServerRegistryServiceTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        _clientFactory.Setup(f => f.InvalidateClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private McpServerRegistryService CreateService() => new(
        _repository.Object,
        _dataProtection,
        _clientFactory.Object,
        _catalog.Object,
        _toolProvider.Object,
        _serviceProvider);

    private void SetupQueryable(List<McpServerRegistration> data)
    {
        var mock = data.BuildMock();
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
    }

    private static McpServerRegistration MakeRegistration(
        string name = "context7",
        string transport = "sse",
        string url = "https://mcp.example.com/sse",
        bool isEnabled = true,
        string? authTokenEncrypted = null,
        string? authType = "bearer") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ServerUrl = url,
        Transport = transport,
        AuthType = authType,
        AuthTokenEncrypted = authTokenEncrypted,
        Priority = 0,
        IsEnabled = isEnabled,
        Description = "test mcp server",
        Tags = "[\"test\"]",
        CreationTime = DateTime.UtcNow
    };

    private string FakeCipher(string plaintext) =>
        _dataProtection.CreateProtector(McpServerRegistration.AuthTokenProtectorPurpose).Protect(plaintext);

    // =====================================================================
    // 查询
    // =====================================================================

    [Fact]
    public async Task GetPagedListAsync_EmptyRepository_ReturnsEmpty()
    {
        SetupQueryable(new List<McpServerRegistration>());
        var svc = CreateService();

        var result = await svc.GetPagedListAsync(new McpServerRegistrationQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Items.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetPagedListAsync_FilterByTransport_ReturnsMatching()
    {
        SetupQueryable(new List<McpServerRegistration>
        {
            MakeRegistration("a", "sse"),
            MakeRegistration("b", "streamable-http"),
        });
        var svc = CreateService();

        var result = await svc.GetPagedListAsync(new McpServerRegistrationQueryDto { Transport = "sse", PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.Count.ShouldBe(1);
        result.Data.Items[0].Transport.ShouldBe("sse");
    }

    [Fact]
    public async Task GetPagedListAsync_NeverExposesEncryptedKey()
    {
        var fakeCipher = FakeCipher("super-secret-token");
        SetupQueryable(new List<McpServerRegistration>
        {
            MakeRegistration(authTokenEncrypted: fakeCipher),
        });
        var svc = CreateService();

        var result = await svc.GetPagedListAsync(new McpServerRegistrationQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        var dto = result.Data!.Items[0];
        dto.HasAuthToken.ShouldBeTrue();
        var json = JsonSerializer.Serialize(dto);
        json.ShouldNotContain("super-secret-token");
        json.ShouldNotContain(fakeCipher);
    }

    // =====================================================================
    // 创建：加密 + 校验
    // =====================================================================

    [Fact]
    public async Task CreateAsync_EncryptsAuthToken_AndReturnsHasAuthTokenTrue()
    {
        SetupQueryable(new List<McpServerRegistration>());
        McpServerRegistration? inserted = null;
        _repository.Setup(r => r.InsertAsync(It.IsAny<McpServerRegistration>(), It.IsAny<CancellationToken>()))
            .Callback<McpServerRegistration, CancellationToken>((p, _) => inserted = p)
            .Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.CreateAsync(new CreateMcpServerRegistrationDto
        {
            Name = "github-mcp",
            ServerUrl = "https://api.githubcopilot.com/mcp/",
            Transport = "streamable-http",
            AuthToken = "ghp-plaintext",
            AuthType = "bearer"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        inserted.ShouldNotBeNull();
        inserted!.AuthTokenEncrypted.ShouldNotBeNull();
        inserted.AuthTokenEncrypted.ShouldNotContain("ghp-plaintext");
        var roundTrip = _dataProtection.CreateProtector(McpServerRegistration.AuthTokenProtectorPurpose).Unprotect(inserted.AuthTokenEncrypted!);
        roundTrip.ShouldBe("ghp-plaintext");
        result.Data!.HasAuthToken.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsConflict()
    {
        SetupQueryable(new List<McpServerRegistration> { MakeRegistration("dup") });
        var svc = CreateService();

        var result = await svc.CreateAsync(new CreateMcpServerRegistrationDto
        {
            Name = "dup",
            ServerUrl = "https://x.example.com",
            Transport = "sse"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

    [Fact]
    public async Task CreateAsync_StdioTransport_IsRejected()
    {
        SetupQueryable(new List<McpServerRegistration>());
        var svc = CreateService();

        var result = await svc.CreateAsync(new CreateMcpServerRegistrationDto
        {
            Name = "local-fs",
            ServerUrl = "https://placeholder.example.com",
            Transport = "stdio"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("stdio servers must be configured via deployment configuration (AI:Mcp options)");
        _repository.Verify(r => r.InsertAsync(It.IsAny<McpServerRegistration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_UnknownTransport_IsRejected()
    {
        SetupQueryable(new List<McpServerRegistration>());
        var svc = CreateService();

        var result = await svc.CreateAsync(new CreateMcpServerRegistrationDto
        {
            Name = "weird",
            ServerUrl = "https://x.example.com",
            Transport = "carrier-pigeon"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("not supported");
    }

    [Fact]
    public async Task CreateAsync_NonHttpServerUrl_IsRejected()
    {
        SetupQueryable(new List<McpServerRegistration>());
        var svc = CreateService();

        var result = await svc.CreateAsync(new CreateMcpServerRegistrationDto
        {
            Name = "bad-url",
            ServerUrl = "not a url",
            Transport = "sse"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("absolute http(s) URI");
    }

    [Fact]
    public async Task CreateAsync_Success_InvalidatesRuntimeCaches()
    {
        SetupQueryable(new List<McpServerRegistration>());
        _repository.Setup(r => r.InsertAsync(It.IsAny<McpServerRegistration>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var svc = CreateService();

        var result = await svc.CreateAsync(new CreateMcpServerRegistrationDto
        {
            Name = "fresh",
            ServerUrl = "https://x.example.com",
            Transport = "sse"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        _catalog.Verify(c => c.Invalidate(), Times.Once);
        _clientFactory.Verify(f => f.InvalidateClientAsync("fresh", It.IsAny<CancellationToken>()), Times.Once);
        _toolProvider.Verify(p => p.InvalidateCache("fresh"), Times.Once);
    }

    // =====================================================================
    // 更新
    // =====================================================================

    [Fact]
    public async Task UpdateAsync_WithoutAuthToken_LeavesEncryptedFieldUnchanged()
    {
        var originalCipher = FakeCipher("existing");
        var existing = MakeRegistration(authTokenEncrypted: originalCipher);
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.UpdateAsync(existing.Id, new UpdateMcpServerRegistrationDto
        {
            Description = "updated desc"
        });

        result.Succeeded.ShouldBeTrue();
        existing.AuthTokenEncrypted.ShouldBe(originalCipher);
        existing.Description.ShouldBe("updated desc");
    }

    [Fact]
    public async Task UpdateAsync_WithNewAuthToken_RotatesEncryptedField()
    {
        var originalCipher = FakeCipher("old");
        var existing = MakeRegistration(authTokenEncrypted: originalCipher);
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.UpdateAsync(existing.Id, new UpdateMcpServerRegistrationDto { AuthToken = "new-token" });

        result.Succeeded.ShouldBeTrue();
        existing.AuthTokenEncrypted.ShouldNotBe(originalCipher);
        var roundTrip = _dataProtection.CreateProtector(McpServerRegistration.AuthTokenProtectorPurpose).Unprotect(existing.AuthTokenEncrypted!);
        roundTrip.ShouldBe("new-token");
    }

    [Fact]
    public async Task UpdateAsync_WithEmptyAuthToken_ClearsEncryptedField()
    {
        var existing = MakeRegistration(authTokenEncrypted: FakeCipher("old"));
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.UpdateAsync(existing.Id, new UpdateMcpServerRegistrationDto { AuthToken = "" });

        result.Succeeded.ShouldBeTrue();
        existing.AuthTokenEncrypted.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateAsync_ChangingTransportToStdio_IsRejected()
    {
        var existing = MakeRegistration();
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.UpdateAsync(existing.Id, new UpdateMcpServerRegistrationDto { Transport = "stdio" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("stdio servers must be configured via deployment configuration (AI:Mcp options)");
        existing.Transport.ShouldBe("sse");
    }

    [Fact]
    public async Task UpdateAsync_Success_InvalidatesRuntimeCaches_IncludingOldName()
    {
        var existing = MakeRegistration("old-name");
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.UpdateAsync(existing.Id, new UpdateMcpServerRegistrationDto { Name = "new-name" });

        result.Succeeded.ShouldBeTrue(result.Message);
        _catalog.Verify(c => c.Invalidate(), Times.Once);
        _clientFactory.Verify(f => f.InvalidateClientAsync("new-name", It.IsAny<CancellationToken>()), Times.Once);
        _clientFactory.Verify(f => f.InvalidateClientAsync("old-name", It.IsAny<CancellationToken>()), Times.Once);
        _toolProvider.Verify(p => p.InvalidateCache("new-name"), Times.Once);
        _toolProvider.Verify(p => p.InvalidateCache("old-name"), Times.Once);
    }

    // =====================================================================
    // 删除
    // =====================================================================

    [Fact]
    public async Task DeleteAsync_NotFound_Returns404()
    {
        SetupQueryable(new List<McpServerRegistration>());
        var svc = CreateService();

        var result = await svc.DeleteAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task DeleteAsync_Existing_CallsRepositoryDelete_AndInvalidatesRuntime()
    {
        var existing = MakeRegistration();
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.DeleteAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        _repository.Verify(r => r.DeleteAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _catalog.Verify(c => c.Invalidate(), Times.Once);
        _clientFactory.Verify(f => f.InvalidateClientAsync(existing.Name, It.IsAny<CancellationToken>()), Times.Once);
        _toolProvider.Verify(p => p.InvalidateCache(existing.Name), Times.Once);
    }

    // =====================================================================
    // TestConnection — 真实探针（经 IMcpClientFactory）
    // =====================================================================

    [Fact]
    public async Task TestConnectionAsync_HealthyServer_ConnectsAndReportsToolCount()
    {
        var existing = MakeRegistration(authTokenEncrypted: FakeCipher("valid-token"));
        SetupQueryable(new List<McpServerRegistration> { existing });

        var adapter = new Mock<IMcpClientAdapter>();
        adapter.Setup(a => a.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AITool>
            {
                AIFunctionFactory.Create(() => "x", "tool_a"),
                AIFunctionFactory.Create(() => "y", "tool_b")
            });
        McpServerConfig? usedConfig = null;
        _clientFactory.Setup(f => f.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .Callback<McpServerConfig, CancellationToken>((c, _) => usedConfig = c)
            .ReturnsAsync(adapter.Object);

        var svc = CreateService();
        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Success.ShouldBeTrue(result.Data.Message);
        result.Data.Message!.ShouldContain("2 tool(s)");
        result.Data.LatencyMs.ShouldBeGreaterThanOrEqualTo(0);

        // 探针使用与 catalog 相同的映射：HTTP 连接 + 解密后的 Bearer 凭证
        usedConfig.ShouldNotBeNull();
        usedConfig!.ConnectionType.ShouldBe(McpConnectionType.Http);
        usedConfig.Endpoint.ShouldBe(existing.ServerUrl);
        usedConfig.Headers["Authorization"].ShouldBe("Bearer valid-token");
    }

    [Fact]
    public async Task TestConnectionAsync_ConnectionFailure_ReportsFailure()
    {
        var existing = MakeRegistration(authType: "none", authTokenEncrypted: null);
        SetupQueryable(new List<McpServerRegistration> { existing });
        _clientFactory.Setup(f => f.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Failed to connect to MCP server 'context7' after 3 attempts."));

        var svc = CreateService();
        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Success.ShouldBeFalse();
        result.Data.Message!.ShouldContain("Connection failed");
    }

    [Fact]
    public async Task TestConnectionAsync_DisabledRegistration_ReportsFailure_WithoutConnecting()
    {
        var existing = MakeRegistration(isEnabled: false);
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Success.ShouldBeFalse();
        result.Data.Message!.ShouldContain("disabled");
        _clientFactory.Verify(f => f.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidUri_ReportsFailure()
    {
        var existing = MakeRegistration(transport: "sse", url: "not a url");
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Success.ShouldBeFalse();
        result.Data.Message!.ShouldContain("URI");
    }

    [Fact]
    public async Task TestConnectionAsync_LegacyStdioRow_ReportsFailure_WithoutConnecting()
    {
        // schema 收紧前可能遗留 stdio 行 — 探针应直接报告不支持，而非尝试连接
        var existing = MakeRegistration(transport: "stdio", url: "https://placeholder.example.com", authType: "none");
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Success.ShouldBeFalse();
        result.Data.Message!.ShouldContain("stdio");
        _clientFactory.Verify(f => f.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidCiphertext_ReportsFailure()
    {
        var existing = MakeRegistration(authTokenEncrypted: "INVALID_NO_MARKER");
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Success.ShouldBeFalse();
        result.Data.Message!.ShouldContain("decrypt");
    }

    [Fact]
    public async Task TestConnectionAsync_AuthTypeRequiresTokenButMissing_ReportsFailure()
    {
        var existing = MakeRegistration(authTokenEncrypted: null, authType: "bearer");
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Success.ShouldBeFalse();
        result.Data.Message!.ShouldContain("auth token");
    }

    /// <summary>
    /// Stub IDataProtectionProvider — wraps plaintext with a marker prefix so tests can
    /// assert ciphertext shape without depending on real DataProtection key infrastructure.
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
