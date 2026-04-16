using Microsoft.AspNetCore.DataProtection;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// ProviderService 单元测试 — 覆盖 CRUD、加密、HasApiKey 暴露语义、以及 TestConnection 探针
/// </summary>
public class ProviderServiceTests
{
    private readonly Mock<IRepository<Provider, Guid>> _repository = new();
    private readonly StubDataProtectionProvider _dataProtection = new();
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

    private ProviderService CreateService() => new(_repository.Object, _dataProtection, _serviceProvider);

    private void SetupQueryable(List<Provider> data)
    {
        var mock = data.BuildMock();
        _repository.As<IQueryable<Provider>>().Setup(q => q.Provider).Returns(mock.Provider);
        _repository.As<IQueryable<Provider>>().Setup(q => q.Expression).Returns(mock.Expression);
        _repository.As<IQueryable<Provider>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _repository.As<IQueryable<Provider>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
        // ProviderService uses _repository.AsQueryable(...) explicitly — return the same mock.
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
    }

    private static Provider MakeProvider(
        string name = "openai",
        string type = "OpenAI",
        bool isEnabled = true,
        string? apiKeyEncrypted = null) => new()
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
        CreationTime = DateTime.UtcNow
    };

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
        // ProviderDto has no ApiKey/ApiKeyEncrypted property — verify by serialization
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
        SetupQueryable(new List<Provider> { MakeProvider("dup") });
        var svc = CreateService();

        var result = await svc.CreateAsync(new CreateProviderDto { Name = "dup", ProviderType = "OpenAI" });

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
