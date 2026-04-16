using Microsoft.AspNetCore.DataProtection;
using Tnzi.AI.Mcp.Services;
using Tnzi.AI.Mcp.Services.Interfaces;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// McpServerRegistryService 单元测试 — 覆盖 CRUD、加密、HasAuthToken 暴露语义、TestConnection 探针
/// </summary>
public class McpServerRegistryServiceTests
{
    private readonly Mock<IRepository<McpServerRegistration, Guid>> _repository = new();
    private readonly StubDataProtectionProvider _dataProtection = new();
    private readonly IServiceProvider _serviceProvider;

    public McpServerRegistryServiceTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private McpServerRegistryService CreateService() => new(_repository.Object, _dataProtection, _serviceProvider);

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
        Command = null,
        Arguments = null,
        AuthType = authType,
        AuthTokenEncrypted = authTokenEncrypted,
        Priority = 0,
        IsEnabled = isEnabled,
        Description = "test mcp server",
        Tags = "[\"test\"]",
        CreationTime = DateTime.UtcNow
    };

    private string FakeCipher(string plaintext) =>
        _dataProtection.CreateProtector("Tnzi.AI.Mcp.ServerCredential").Protect(plaintext);

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
            MakeRegistration("b", "stdio"),
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
        var roundTrip = _dataProtection.CreateProtector("Tnzi.AI.Mcp.ServerCredential").Unprotect(inserted.AuthTokenEncrypted!);
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
            ServerUrl = "https://x",
            Transport = "sse"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

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
        var roundTrip = _dataProtection.CreateProtector("Tnzi.AI.Mcp.ServerCredential").Unprotect(existing.AuthTokenEncrypted!);
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
    public async Task DeleteAsync_NotFound_Returns404()
    {
        SetupQueryable(new List<McpServerRegistration>());
        var svc = CreateService();

        var result = await svc.DeleteAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task DeleteAsync_Existing_CallsRepositoryDelete()
    {
        var existing = MakeRegistration();
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.DeleteAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        _repository.Verify(r => r.DeleteAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TestConnectionAsync_EnabledWithValidToken_ReturnsSuccess()
    {
        var existing = MakeRegistration(authTokenEncrypted: FakeCipher("valid-token"));
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Success.ShouldBeTrue();
        result.Data.LatencyMs.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task TestConnectionAsync_DisabledRegistration_ReportsFailure()
    {
        var existing = MakeRegistration(isEnabled: false);
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Success.ShouldBeFalse();
        result.Data.Message!.ShouldContain("disabled");
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
    public async Task TestConnectionAsync_StdioTransport_BypassesUriCheck()
    {
        // stdio transport does not require a valid HTTP URL — the URL field may be a placeholder.
        var existing = MakeRegistration(transport: "stdio", url: "local://stdio", authType: "none");
        SetupQueryable(new List<McpServerRegistration> { existing });
        var svc = CreateService();

        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Success.ShouldBeTrue();
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
