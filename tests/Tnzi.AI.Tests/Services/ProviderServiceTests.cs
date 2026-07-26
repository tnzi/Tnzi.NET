using Microsoft.AspNetCore.DataProtection;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// ProviderService 单元测试 - 覆盖 CRUD、加密、HasApiKey 暴露语义、TestConnection 探针
/// 以及 ResourceScope 多租户可见性合并逻辑。
/// </summary>
public class ProviderServiceTests
{
    private readonly Mock<IRepository<Provider, Guid>> _repository = new();
    private readonly StubDataProtectionProvider _dataProtection = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly IServiceProvider _serviceProvider;

    public ProviderServiceTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private ProviderService CreateService(ICurrentTenant? currentTenant = null) =>
        new(_repository.Object, _dataProtection, _httpClientFactory.Object, _serviceProvider, currentTenant);

    private void SetupQueryable(List<Provider> data)
    {
        var mock = data.BuildMock();
        _repository.As<IQueryable<Provider>>().Setup(q => q.Provider).Returns(mock.Provider);
        _repository.As<IQueryable<Provider>>().Setup(q => q.Expression).Returns(mock.Expression);
        _repository.As<IQueryable<Provider>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _repository.As<IQueryable<Provider>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
        // ProviderService uses _repository.AsQueryable(...) explicitly - return the same mock.
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
    }

    private static Provider MakeProvider(
        string name = "openai",
        string type = "OpenAI",
        bool isEnabled = true,
        string? apiKeyEncrypted = null,
        ResourceScope scope = ResourceScope.System,
        Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ProviderType = type,
        Endpoint = "https://api.openai.com/v1",
        DefaultModel = "gpt-4",
        Priority = 0,
        IsEnabled = isEnabled,
        Description = "test provider",
        ApiKeyEncrypted = apiKeyEncrypted,
        CreationTime = DateTime.UtcNow,
        Scope = scope,
        TenantId = tenantId
    };

    // -------------------------------------------------------------------------
    // Visibility (ResourceScope) tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPagedListAsync_WithTenant_ReturnsSystemAndOwnTenantOnly()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        SetupQueryable(new List<Provider>
        {
            MakeProvider("system-1", scope: ResourceScope.System),
            MakeProvider("tenant-a", scope: ResourceScope.Tenant, tenantId: tenantA),
            MakeProvider("tenant-b", scope: ResourceScope.Tenant, tenantId: tenantB),
        });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.TotalCount.ShouldBe(2);
        result.Data.Items.Select(p => p.Name).ShouldContain("system-1");
        result.Data.Items.Select(p => p.Name).ShouldContain("tenant-a");
        result.Data.Items.Select(p => p.Name).ShouldNotContain("tenant-b");
    }

    [Fact]
    public async Task GetPagedListAsync_NoTenant_ReturnsAll()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        SetupQueryable(new List<Provider>
        {
            MakeProvider("system-1", scope: ResourceScope.System),
            MakeProvider("tenant-a", scope: ResourceScope.Tenant, tenantId: tenantA),
            MakeProvider("tenant-b", scope: ResourceScope.Tenant, tenantId: tenantB),
        });

        // No ICurrentTenant injected - single-tenant mode, return all
        var svc = CreateService(currentTenant: null);

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task GetOptionsAsync_WithTenant_ReturnsSystemAndOwnTenantOnly()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        SetupQueryable(new List<Provider>
        {
            MakeProvider("system-1", scope: ResourceScope.System),
            MakeProvider("tenant-a", scope: ResourceScope.Tenant, tenantId: tenantA),
            MakeProvider("tenant-b", scope: ResourceScope.Tenant, tenantId: tenantB),
        });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.GetOptionsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2);
        result.Data.Select(p => p.Name).ShouldContain("system-1");
        result.Data.Select(p => p.Name).ShouldContain("tenant-a");
        result.Data.Select(p => p.Name).ShouldNotContain("tenant-b");
    }

    [Fact]
    public async Task ProviderDto_ContainsScope_AfterCreate()
    {
        SetupQueryable(new List<Provider>());
        _repository.Setup(r => r.InsertAsync(It.IsAny<Provider>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tenantA = Guid.NewGuid();
        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.CreateAsync(new CreateProviderDto
        {
            Name = "my-provider",
            ProviderType = "OpenAI"
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.Scope.ShouldBe(ResourceScope.Tenant);
        result.Data.TenantId.ShouldBe(tenantA);
    }

    [Fact]
    public async Task CreateAsync_NoTenant_DefaultsToSystemScope()
    {
        SetupQueryable(new List<Provider>());
        _repository.Setup(r => r.InsertAsync(It.IsAny<Provider>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = CreateService(currentTenant: null);

        var result = await svc.CreateAsync(new CreateProviderDto
        {
            Name = "global-provider",
            ProviderType = "Anthropic"
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.Scope.ShouldBe(ResourceScope.System);
        result.Data.TenantId.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Existing CRUD / encryption tests (unchanged)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPagedListAsync_EmptyRepository_ReturnsEmpty()
    {
        SetupQueryable(new List<Provider>());
        var svc = CreateService();

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Items.Count.ShouldBe(0);
        result.Data.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetPagedListAsync_FilterByProviderType_ReturnsMatching()
    {
        SetupQueryable(new List<Provider>
        {
            MakeProvider("openai-1", "OpenAI"),
            MakeProvider("anthropic-1", "Anthropic"),
        });
        var svc = CreateService();

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { ProviderType = "OpenAI", PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.Count.ShouldBe(1);
        result.Data.Items[0].ProviderType.ShouldBe("OpenAI");
    }

    [Fact]
    public async Task GetPagedListAsync_NeverExposesEncryptedKey()
    {
        // Use the real protector to produce a realistic ciphertext.
        var fakeCipher = FakeCipher("sk-secret");
        SetupQueryable(new List<Provider>
        {
            MakeProvider(apiKeyEncrypted: fakeCipher),
        });
        var svc = CreateService();

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        var dto = result.Data!.Items[0];
        dto.HasApiKey.ShouldBeTrue();
        // ProviderDto has no ApiKey/ApiKeyEncrypted property - verify by serialization
        var json = JsonSerializer.Serialize(dto);
        json.ShouldNotContain("sk-secret");
        json.ShouldNotContain(fakeCipher);
    }

    [Fact]
    public async Task CreateAsync_EncryptsApiKey_AndReturnsHasApiKeyTrue()
    {
        SetupQueryable(new List<Provider>());
        Provider? inserted = null;
        _repository.Setup(r => r.InsertAsync(It.IsAny<Provider>(), It.IsAny<CancellationToken>()))
            .Callback<Provider, CancellationToken>((p, _) => inserted = p)
            .Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.CreateAsync(new CreateProviderDto
        {
            Name = "openai-prod",
            ProviderType = "OpenAI",
            ApiKey = "sk-plaintext-key",
            DefaultModel = "gpt-4"
        });

        result.Succeeded.ShouldBeTrue();
        inserted.ShouldNotBeNull();
        inserted!.ApiKeyEncrypted.ShouldNotBeNull();
        inserted.ApiKeyEncrypted.ShouldNotContain("sk-plaintext-key"); // ciphertext, not plaintext
        // Round-trip via the same protector: must yield the original plaintext.
        var roundTrip = _dataProtection.CreateProtector("Tnzi.AI.Providers.ApiKey").Unprotect(inserted.ApiKeyEncrypted!);
        roundTrip.ShouldBe("sk-plaintext-key");
        result.Data!.HasApiKey.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        // Seed a System-scope provider; creating another with same name + System scope conflicts
        SetupQueryable(new List<Provider> { MakeProvider("dup", scope: ResourceScope.System) });
        var svc = CreateService();

        var result = await svc.CreateAsync(new CreateProviderDto { Name = "dup", ProviderType = "OpenAI", Scope = ResourceScope.System });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

    private string FakeCipher(string plaintext) =>
        _dataProtection.CreateProtector("Tnzi.AI.Providers.ApiKey").Protect(plaintext);

    [Fact]
    public async Task UpdateAsync_WithoutApiKey_LeavesEncryptedFieldUnchanged()
    {
        var originalCipher = FakeCipher("existing");
        var existing = MakeProvider(apiKeyEncrypted: originalCipher);
        SetupQueryable(new List<Provider> { existing });
        var svc = CreateService();

        var result = await svc.UpdateAsync(existing.Id, new UpdateProviderDto
        {
            Description = "updated desc"
            // ApiKey omitted → keep existing
        });

        result.Succeeded.ShouldBeTrue();
        existing.ApiKeyEncrypted.ShouldBe(originalCipher);
        existing.Description.ShouldBe("updated desc");
    }

    [Fact]
    public async Task UpdateAsync_WithNewApiKey_RotatesEncryptedField()
    {
        var originalCipher = FakeCipher("old");
        var existing = MakeProvider(apiKeyEncrypted: originalCipher);
        SetupQueryable(new List<Provider> { existing });
        var svc = CreateService();

        var result = await svc.UpdateAsync(existing.Id, new UpdateProviderDto { ApiKey = "sk-new-key" });

        result.Succeeded.ShouldBeTrue();
        existing.ApiKeyEncrypted.ShouldNotBe(originalCipher);
        var roundTrip = _dataProtection.CreateProtector("Tnzi.AI.Providers.ApiKey").Unprotect(existing.ApiKeyEncrypted!);
        roundTrip.ShouldBe("sk-new-key");
    }

    [Fact]
    public async Task UpdateAsync_WithEmptyApiKey_ClearsEncryptedField()
    {
        var existing = MakeProvider(apiKeyEncrypted: FakeCipher("old"));
        SetupQueryable(new List<Provider> { existing });
        var svc = CreateService();

        var result = await svc.UpdateAsync(existing.Id, new UpdateProviderDto { ApiKey = "" });

        result.Succeeded.ShouldBeTrue();
        existing.ApiKeyEncrypted.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Write-path isolation tests (cross-tenant + system-tamper guard)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_TenantA_OnTenantBProvider_Returns404()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantBProvider = MakeProvider("b-provider", scope: ResourceScope.Tenant, tenantId: tenantB);
        SetupQueryable(new List<Provider> { tenantBProvider });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.UpdateAsync(tenantBProvider.Id, new UpdateProviderDto { Description = "hacked" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task DeleteAsync_TenantA_OnTenantBProvider_Returns404()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantBProvider = MakeProvider("b-provider", scope: ResourceScope.Tenant, tenantId: tenantB);
        SetupQueryable(new List<Provider> { tenantBProvider });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.DeleteAsync(tenantBProvider.Id);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task UpdateAsync_TenantA_OnSystemProvider_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var systemProvider = MakeProvider("shared-system", scope: ResourceScope.System);
        SetupQueryable(new List<Provider> { systemProvider });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.UpdateAsync(systemProvider.Id, new UpdateProviderDto { Description = "tampered" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    [Fact]
    public async Task DeleteAsync_TenantA_OnSystemProvider_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var systemProvider = MakeProvider("shared-system", scope: ResourceScope.System);
        SetupQueryable(new List<Provider> { systemProvider });

        var svc = CreateService(new StubCurrentTenant(tenantA));

        var result = await svc.DeleteAsync(systemProvider.Id);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    [Fact]
    public async Task UpdateAsync_SingleTenantMode_CanUpdateSystemProvider()
    {
        var systemProvider = MakeProvider("global-system", scope: ResourceScope.System);
        SetupQueryable(new List<Provider> { systemProvider });
        _repository.Setup(r => r.UpdateAsync(It.IsAny<Provider>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // No ICurrentTenant - single-tenant mode, no guard applies
        var svc = CreateService(currentTenant: null);

        var result = await svc.UpdateAsync(systemProvider.Id, new UpdateProviderDto { Description = "admin update" });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteAsync_SingleTenantMode_CanDeleteSystemProvider()
    {
        var systemProvider = MakeProvider("global-system", scope: ResourceScope.System);
        SetupQueryable(new List<Provider> { systemProvider });

        // No ICurrentTenant - single-tenant mode, no guard applies
        var svc = CreateService(currentTenant: null);

        var result = await svc.DeleteAsync(systemProvider.Id);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteAsync_NotFound_Returns404()
    {
        SetupQueryable(new List<Provider>());
        var svc = CreateService();

        var result = await svc.DeleteAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task DeleteAsync_Existing_CallsRepositoryDelete()
    {
        var existing = MakeProvider();
        SetupQueryable(new List<Provider> { existing });
        var svc = CreateService();

        var result = await svc.DeleteAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        _repository.Verify(r => r.DeleteAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TestConnectionAsync_EnabledWithValidKey_ReturnsSuccess()
    {
        var existing = MakeProvider(apiKeyEncrypted: FakeCipher("sk-real"));
        SetupQueryable(new List<Provider> { existing });
        var svc = CreateService();

        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Success.ShouldBeTrue();
        result.Data.LatencyMs.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task TestConnectionAsync_DisabledProvider_ReportsFailure()
    {
        var existing = MakeProvider(isEnabled: false);
        SetupQueryable(new List<Provider> { existing });
        var svc = CreateService();

        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();   // service call OK
        result.Data!.Success.ShouldBeFalse(); // probe fails
        result.Data.Message!.ShouldContain("disabled");
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidCiphertext_ReportsFailure()
    {
        // Use a cipher that the stub protector cannot Unprotect (does not start with marker)
        var existing = MakeProvider(apiKeyEncrypted: "INVALID_CIPHERTEXT_NO_MARKER");
        SetupQueryable(new List<Provider> { existing });
        var svc = CreateService();

        var result = await svc.TestConnectionAsync(existing.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Success.ShouldBeFalse();
        result.Data.Message!.ShouldContain("decrypt");
    }

    // -------------------------------------------------------------------------
    // Stubs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stub IDataProtectionProvider - wraps plaintext with a marker prefix so tests can
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

    /// <summary>
    /// Stub ICurrentTenant - 返回固定租户 ID。
    /// </summary>
    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid tenantId) { Id = tenantId; }
        public Guid? Id { get; }
        public string? Name => null;
        public bool IsAvailable => true;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOpDisposable();
        private sealed class NoOpDisposable : IDisposable { public void Dispose() { } }
    }
}
