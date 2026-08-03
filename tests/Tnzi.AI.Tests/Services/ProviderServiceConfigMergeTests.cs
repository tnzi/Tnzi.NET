using Microsoft.AspNetCore.DataProtection;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// ProviderService 双源合并（Database + Configuration）单元测试 -
/// 覆盖配置条目合并/置顶/分页、同名覆盖隐藏、只读写保护（400）、
/// API Key 永不泄漏、options 下拉合并。
/// </summary>
public class ProviderServiceConfigMergeTests
{
    private readonly Mock<IRepository<Provider, Guid>> _repository = new();
    private readonly StubDataProtectionProvider _dataProtection = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly IServiceProvider _serviceProvider;

    public ProviderServiceConfigMergeTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private ProviderService CreateService(AIOptions? aiOptions = null) =>
        new(_repository.Object, _dataProtection, _httpClientFactory.Object, _serviceProvider,
            currentTenant: null,
            aiOptionsMonitor: aiOptions == null ? null : new StubOptionsMonitor(aiOptions));

    private void SetupQueryable(List<Provider> data)
    {
        var mock = data.BuildMock();
        _repository.As<IQueryable<Provider>>().Setup(q => q.Provider).Returns(mock.Provider);
        _repository.As<IQueryable<Provider>>().Setup(q => q.Expression).Returns(mock.Expression);
        _repository.As<IQueryable<Provider>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _repository.As<IQueryable<Provider>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
    }

    private static Provider MakeProvider(string name, bool isEnabled = true, int priority = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ProviderType = "OpenAI",
        Endpoint = "https://api.openai.com/v1",
        DefaultModel = "gpt-4o",
        Priority = priority,
        IsEnabled = isEnabled,
        CreationTime = DateTime.UtcNow,
        Scope = ResourceScope.System
    };

    private static AIOptions MakeAiOptions(params (string Name, bool Enabled, string? ApiKey)[] providers)
    {
        var options = new AIOptions();
        foreach (var (name, enabled, apiKey) in providers)
        {
            options.Providers[name] = new ProviderOptions
            {
                Name = name,
                Enabled = enabled,
                ApiKey = apiKey,
                BaseUrl = "https://api.deepseek.com/v1",
                DefaultModel = "deepseek-chat"
            };
        }
        return options;
    }

    // -------------------------------------------------------------------------
    // Merge + source flags
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPagedListAsync_MergesConfigurationProviders_PinnedFirst()
    {
        SetupQueryable(new List<Provider> { MakeProvider("db-openai") });
        var svc = CreateService(MakeAiOptions(("DeepSeek", true, "sk-config-secret")));

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.TotalCount.ShouldBe(2);
        result.Data.Items.Count.ShouldBe(2);
        // Config entry pinned ahead of DB entries
        result.Data.Items[0].Name.ShouldBe("DeepSeek");
        result.Data.Items[0].Source.ShouldBe(ProviderSources.Configuration);
        result.Data.Items[0].Id.ShouldBe(ProviderService.GetConfigurationProviderId("DeepSeek"));
        result.Data.Items[0].HasApiKey.ShouldBeTrue();
        result.Data.Items[0].CreationTime.ShouldBeNull();
        result.Data.Items[1].Name.ShouldBe("db-openai");
        result.Data.Items[1].Source.ShouldBe(ProviderSources.Database);
    }

    [Fact]
    public async Task GetPagedListAsync_NoAiOptions_BehavesAsDatabaseOnly()
    {
        SetupQueryable(new List<Provider> { MakeProvider("db-openai") });
        var svc = CreateService(aiOptions: null);

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(1);
        result.Data.Items[0].Source.ShouldBe(ProviderSources.Database);
    }

    [Fact]
    public async Task GetPagedListAsync_ConfigEntry_NeverLeaksApiKey()
    {
        SetupQueryable(new List<Provider>());
        var svc = CreateService(MakeAiOptions(("DeepSeek", true, "sk-config-secret")));

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 10 });

        result.Succeeded.ShouldBeTrue();
        var dto = result.Data!.Items[0];
        dto.HasApiKey.ShouldBeTrue();
        var json = JsonSerializer.Serialize(dto);
        json.ShouldNotContain("sk-config-secret");
    }

    [Fact]
    public async Task GetPagedListAsync_ConfigEntryWithoutKey_HasApiKeyFalse()
    {
        SetupQueryable(new List<Provider>());
        var svc = CreateService(MakeAiOptions(("Ollama", true, null)));

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 10 });

        result.Data!.Items[0].HasApiKey.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Same-name override semantics
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPagedListAsync_EnabledDbRowWithSameName_HidesConfigEntry()
    {
        SetupQueryable(new List<Provider> { MakeProvider("DeepSeek", isEnabled: true) });
        var svc = CreateService(MakeAiOptions(("deepseek", true, "sk-x")));

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 10 });

        result.Data!.TotalCount.ShouldBe(1);
        result.Data.Items[0].Source.ShouldBe(ProviderSources.Database);
    }

    [Fact]
    public async Task GetPagedListAsync_DisabledDbRowWithSameName_KeepsConfigEntry()
    {
        // Runtime falls back to config when the DB row is disabled - admin list mirrors that.
        SetupQueryable(new List<Provider> { MakeProvider("DeepSeek", isEnabled: false) });
        var svc = CreateService(MakeAiOptions(("DeepSeek", true, "sk-x")));

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 10 });

        result.Data!.TotalCount.ShouldBe(2);
        result.Data.Items.Count(p => p.Source == ProviderSources.Configuration).ShouldBe(1);
        result.Data.Items.Count(p => p.Source == ProviderSources.Database).ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Merge-then-page arithmetic
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPagedListAsync_MergedPagination_SpansConfigAndDbPages()
    {
        SetupQueryable(new List<Provider>
        {
            MakeProvider("db-1", priority: 3),
            MakeProvider("db-2", priority: 2),
            MakeProvider("db-3", priority: 1),
        });
        var svc = CreateService(MakeAiOptions(("Alpha", true, null), ("Beta", true, null)));

        var page1 = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 1, PageSize = 2 });
        var page2 = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 2, PageSize = 2 });
        var page3 = await svc.GetPagedListAsync(new ProviderQueryDto { PageIndex = 3, PageSize = 2 });

        page1.Data!.TotalCount.ShouldBe(5);
        page1.Data.Items.Select(p => p.Name).ShouldBe(new[] { "Alpha", "Beta" });
        page2.Data!.Items.Select(p => p.Name).ShouldBe(new[] { "db-1", "db-2" });
        page3.Data!.Items.Select(p => p.Name).ShouldBe(new[] { "db-3" });
    }

    [Fact]
    public async Task GetPagedListAsync_KeywordFilter_AppliesToConfigEntries()
    {
        SetupQueryable(new List<Provider> { MakeProvider("db-openai") });
        var svc = CreateService(MakeAiOptions(("DeepSeek", true, null), ("Moonshot", true, null)));

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { Keyword = "deepseek", PageIndex = 1, PageSize = 10 });

        result.Data!.TotalCount.ShouldBe(1);
        result.Data.Items[0].Name.ShouldBe("DeepSeek");
        result.Data.Items[0].Source.ShouldBe(ProviderSources.Configuration);
    }

    [Fact]
    public async Task GetPagedListAsync_IsEnabledFilter_AppliesToConfigEntries()
    {
        SetupQueryable(new List<Provider>());
        var svc = CreateService(MakeAiOptions(("Active", true, null), ("Inactive", false, null)));

        var result = await svc.GetPagedListAsync(new ProviderQueryDto { IsEnabled = true, PageIndex = 1, PageSize = 10 });

        result.Data!.TotalCount.ShouldBe(1);
        result.Data.Items[0].Name.ShouldBe("Active");
    }

    // -------------------------------------------------------------------------
    // Read-only protection for configuration entries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ConfigId_ReturnsConfigurationDto()
    {
        SetupQueryable(new List<Provider>());
        var svc = CreateService(MakeAiOptions(("DeepSeek", true, "sk-x")));

        var result = await svc.GetByIdAsync(ProviderService.GetConfigurationProviderId("DeepSeek"));

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Source.ShouldBe(ProviderSources.Configuration);
        result.Data.Name.ShouldBe("DeepSeek");
    }

    [Fact]
    public async Task UpdateAsync_ConfigId_Returns400()
    {
        SetupQueryable(new List<Provider>());
        var svc = CreateService(MakeAiOptions(("DeepSeek", true, null)));

        var result = await svc.UpdateAsync(
            ProviderService.GetConfigurationProviderId("DeepSeek"),
            new UpdateProviderDto { Description = "tamper" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("read-only");
    }

    [Fact]
    public async Task DeleteAsync_ConfigId_Returns400()
    {
        SetupQueryable(new List<Provider>());
        var svc = CreateService(MakeAiOptions(("DeepSeek", true, null)));

        var result = await svc.DeleteAsync(ProviderService.GetConfigurationProviderId("DeepSeek"));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("read-only");
        _repository.Verify(r => r.DeleteAsync(It.IsAny<Provider>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TestConnectionAsync_ConfigId_Returns400()
    {
        SetupQueryable(new List<Provider>());
        var svc = CreateService(MakeAiOptions(("DeepSeek", true, null)));

        var result = await svc.TestConnectionAsync(ProviderService.GetConfigurationProviderId("DeepSeek"));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    // -------------------------------------------------------------------------
    // Options dropdown merge
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOptionsAsync_AppendsEnabledConfigProviders()
    {
        SetupQueryable(new List<Provider> { MakeProvider("db-openai") });
        var svc = CreateService(MakeAiOptions(("DeepSeek", true, null), ("Disabled", false, null)));

        var result = await svc.GetOptionsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2);
        result.Data[0].Name.ShouldBe("db-openai");
        result.Data[0].Source.ShouldBe(ProviderSources.Database);
        result.Data[1].Name.ShouldBe("DeepSeek");
        result.Data[1].Source.ShouldBe(ProviderSources.Configuration);
        result.Data.ShouldNotContain(o => o.Name == "Disabled");
    }

    [Fact]
    public async Task GetOptionsAsync_EnabledDbRowWithSameName_HidesConfigOption()
    {
        SetupQueryable(new List<Provider> { MakeProvider("DeepSeek") });
        var svc = CreateService(MakeAiOptions(("deepseek", true, null)));

        var result = await svc.GetOptionsAsync();

        result.Data!.Count.ShouldBe(1);
        result.Data[0].Source.ShouldBe(ProviderSources.Database);
    }

    // -------------------------------------------------------------------------
    // Deterministic configuration id
    // -------------------------------------------------------------------------

    [Fact]
    public void GetConfigurationProviderId_IsDeterministic_AndCaseInsensitive()
    {
        var a = ProviderService.GetConfigurationProviderId("DeepSeek");
        var b = ProviderService.GetConfigurationProviderId("deepseek");
        var c = ProviderService.GetConfigurationProviderId(" DeepSeek ");
        var other = ProviderService.GetConfigurationProviderId("OpenAI");

        a.ShouldBe(b);
        a.ShouldBe(c);
        a.ShouldNotBe(other);
        a.ShouldNotBe(Guid.Empty);
    }

    // -------------------------------------------------------------------------
    // Stubs
    // -------------------------------------------------------------------------

    private sealed class StubOptionsMonitor : IOptionsMonitor<AIOptions>
    {
        public StubOptionsMonitor(AIOptions value) { CurrentValue = value; }
        public AIOptions CurrentValue { get; }
        public AIOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AIOptions, string?> listener) => null;
    }

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
