using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Tnzi.AI.Tests.Integration;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// 数据库来源的 Provider 三处名实不符（2026-09-12）：
/// ① 密钥轮换：客户端缓存键必须带凭据指纹，另一实例的 DB 缓存到 TTL 重读到新密钥后不能再命中旧客户端；
/// ② <c>Provider.ProviderType</c> 决定协议：工厂按它选 <see cref="IChatClientProvider"/>，其次才按名字；
/// ③ 只在 admin 登记的名字要拿到 Fallback 弹性管线，不是未注册的裸 HttpClient。
/// 真实 SQLite + EFCoreRepository，不 mock 被测对象。
/// </summary>
public class ChatClientFactoryProviderContractTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly RecordingProvider _openAi = new("OpenAI");
    private readonly RecordingProvider _anthropic = new("Anthropic");
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();

    public ChatClientFactoryProviderContractTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentUser>(_ =>
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(Guid.Empty);
            user.Setup(u => u.TenantId).Returns((Guid?)null);
            return user.Object;
        });
        services.AddScoped<ICurrentTenant>(_ => new CurrentTenant());
        services.AddSingleton(MsOptions.Create(new MultiTenancyOptions { Enabled = false }));
        services.AddDbContext<SharedResourceMtDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<IRepository<Provider, Guid>, EFCoreRepository<SharedResourceMtDbContext, Provider, Guid>>();
        services.AddSingleton(_dataProtection);
        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SharedResourceMtDbContext>();
        ctx.Database.EnsureCreated();
        ctx.Set<Provider>().AddRange(
            new Provider
            {
                Name = "Rotating", ProviderType = "OpenAI", IsEnabled = true,
                Endpoint = "https://rotating.example/v1", ApiKeyEncrypted = Protect("key-v1"),
                Scope = ResourceScope.System, DefaultModel = "gpt-4o"
            },
            new Provider
            {
                Name = "MyClaude", ProviderType = "Anthropic", IsEnabled = true,
                Endpoint = "https://api.anthropic.com", ApiKeyEncrypted = Protect("claude-key"),
                Scope = ResourceScope.System, DefaultModel = "claude-sonnet-4"
            });
        ctx.SaveChangesAsync().GetAwaiter().GetResult();
    }

    private string Protect(string key) => _dataProtection.CreateProtector(Provider.ApiKeyProtectorPurpose).Protect(key);

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private ChatClientFactory CreateFactory()
    {
        var options = new AIOptions
        {
            DefaultProvider = "OpenAI",
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["OpenAI"] = new() { Name = "OpenAI", Enabled = true, ApiKey = "config-key", DefaultModel = "gpt-4o" }
            }
        };
        return new ChatClientFactory(
            new StaticOptionsMonitor<AIOptions>(options),
            [_openAi, _anthropic],
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChatClientFactory>.Instance);
    }

    private void RotateKey(string providerName, string newKey)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SharedResourceMtDbContext>();
        var row = ctx.Set<Provider>().Single(p => p.Name == providerName);
        row.ApiKeyEncrypted = Protect(newKey);
        ctx.SaveChangesAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public void GetChatClient_AfterDbApiKeyRotation_OnTtlBoundary_CreatesClientWithNewKey()
    {
        // 另一实例的形态：没人调 InvalidateProvider，只有 DB 解析缓存到 TTL 重读；
        // 客户端缓存键不含凭据时，新密钥读到了、旧客户端照样命中，401 一直到重启。
        var factory = CreateFactory();
        var before = factory.GetChatClient("Rotating");
        _openAi.LastOptions!.ApiKey.ShouldBe("key-v1");

        RotateKey("Rotating", "key-v2");
        factory.ExpireDatabaseProviderCacheForTesting();

        var after = factory.GetChatClient("Rotating");
        after.ShouldNotBeSameAs(before);
        _openAi.LastOptions!.ApiKey.ShouldBe("key-v2");
    }

    [Fact]
    public void GetChatClient_SameKey_OnTtlBoundary_ReusesClient()
    {
        var factory = CreateFactory();
        var before = factory.GetChatClient("Rotating");

        factory.ExpireDatabaseProviderCacheForTesting();

        factory.GetChatClient("Rotating").ShouldBeSameAs(before);
        _openAi.CreateCount.ShouldBe(1);
    }

    [Fact]
    public void GetChatClient_DbRowWithProviderType_SelectsThatProtocol()
    {
        // admin 建 Name=MyClaude / ProviderType=Anthropic：此前按名字匹配不到就回退 OpenAI 兼容客户端打 api.anthropic.com
        var factory = CreateFactory();

        factory.GetChatClient("MyClaude");

        _anthropic.CreateCount.ShouldBe(1);
        _openAi.CreateCount.ShouldBe(0);
        _anthropic.LastOptions!.ProviderType.ShouldBe("Anthropic");
        _anthropic.LastOptions.ApiKey.ShouldBe("claude-key");
    }

    [Fact]
    public async Task GetChatClient_GlmEndpoint_IsWrappedWithThinkTagProcessor()
    {
        // 配置条目叫 "Zhipu"、经 OpenAI 兼容端点接入：工厂要按形态选出 glm 处理器包在 SDK 客户端外面
        var options = new AIOptions
        {
            DefaultProvider = "Zhipu",
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["Zhipu"] = new() { Name = "Zhipu", Enabled = true, ApiKey = "k", DefaultModel = "glm-z1-flash", BaseUrl = "https://open.bigmodel.cn/api/paas/v4" },
                ["OpenAI"] = new() { Name = "OpenAI", Enabled = true, ApiKey = "k", DefaultModel = "gpt-4o" }
            }
        };
        var inner = new Mock<IChatClient>();
        inner.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "<think>draft</think>final")));
        var provider = new Mock<IChatClientProvider>();
        provider.Setup(p => p.ProviderName).Returns("OpenAI");
        provider.Setup(p => p.CreateChatClient(It.IsAny<ProviderOptions>(), It.IsAny<string>())).Returns(inner.Object);

        var factory = new ChatClientFactory(
            new StaticOptionsMonitor<AIOptions>(options),
            [provider.Object],
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChatClientFactory>.Instance,
            [new GlmChatMessageProcessor(), new KimiChatMessageProcessor()]);

        var glm = factory.GetChatClient("Zhipu");
        var plain = factory.GetChatClient("OpenAI");

        glm.ShouldBeOfType<MessageProcessingChatClient>().Processor.ProviderName.ShouldBe("glm");
        plain.ShouldNotBeOfType<MessageProcessingChatClient>();
        (await glm.GetResponseAsync([new ChatMessage(ChatRole.User, "?")])).Text.ShouldBe("final");
    }

    [Fact]
    public void ResilientHttpClientNames_UnregisteredName_ReturnsFallback()
    {
        ResilientHttpClientNames.Register("Configured");

        ResilientHttpClientNames.For("Configured").ShouldBe($"{ResilientHttpClientNames.Fallback}:Configured");
        ResilientHttpClientNames.For("configured").ShouldBe($"{ResilientHttpClientNames.Fallback}:configured");
        ResilientHttpClientNames.For("OnlyInAdmin").ShouldBe(ResilientHttpClientNames.Fallback);
        ResilientHttpClientNames.For(null).ShouldBe(ResilientHttpClientNames.Fallback);
    }

    private sealed class RecordingProvider(string providerName) : IChatClientProvider
    {
        public string ProviderName => providerName;
        public ProviderOptions? LastOptions { get; private set; }
        public int CreateCount { get; private set; }

        public IChatClient CreateChatClient(ProviderOptions options, string model)
        {
            LastOptions = options;
            CreateCount++;
            return Mock.Of<IChatClient>();
        }

        public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(ProviderOptions options, string model) => null;

        public object CreateNativeClient(ProviderOptions options) => new object();
    }
}
