using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Tnzi.AI.Tests.Integration;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// <see cref="ChatClientFactory"/> 按名解析数据库 Provider 时必须带上租户维度：
/// 唯一索引是 (Name, Scope, TenantId)，两个租户可以各自登记同名 Provider（端点与密钥都不同），
/// 而工厂是 Singleton、缓存按名分桶 —— 先解析的那个租户会把自己的端点与密钥留给所有租户。
/// 真实 SQLite + EFCoreRepository，不 mock 被测对象。
/// </summary>
public class ChatClientFactoryTenantIsolationTests : IDisposable
{
    private static readonly AsyncLocal<Guid?> AmbientTenant = new();

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly RecordingChatClientProvider _provider = new();
    private readonly CapturingLogger _log = new();
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public ChatClientFactoryTenantIsolationTests()
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
        // 工厂在自己开的作用域里读 ICurrentTenant；这里让它读测试的 AsyncLocal，模拟环境租户随流动传播
        services.AddScoped<ICurrentTenant>(_ => new StubCurrentTenant(AmbientTenant.Value));
        services.AddSingleton(MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
        services.AddDbContext<SharedResourceMtDbContext>(o => o
            .UseSqlite(_connection)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>());
        services.AddScoped<IRepository<Provider, Guid>, EFCoreRepository<SharedResourceMtDbContext, Provider, Guid>>();
        services.AddSingleton<IDataProtectionProvider>(_dataProtection);
        _serviceProvider = services.BuildServiceProvider();
        var protector = _dataProtection.CreateProtector(Provider.ApiKeyProtectorPurpose);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SharedResourceMtDbContext>();
        ctx.Database.EnsureCreated();
        ctx.Set<Provider>().AddRange(
            new Provider
            {
                Name = "OpenAI", ProviderType = "OpenAI", IsEnabled = true, Priority = 100,
                Endpoint = "https://tenant-a.example/v1", ApiKeyEncrypted = protector.Protect("key-a"),
                Scope = ResourceScope.Tenant, TenantId = _tenantA
            },
            new Provider
            {
                Name = "Shared", ProviderType = "OpenAI", IsEnabled = true,
                Endpoint = "https://shared.example/v1", ApiKeyEncrypted = protector.Protect("key-shared"),
                Scope = ResourceScope.System, TenantId = null
            },
            new Provider
            {
                Name = "Shared", ProviderType = "OpenAI", IsEnabled = true, Priority = 5,
                Endpoint = "https://tenant-b-shared.example/v1", ApiKeyEncrypted = protector.Protect("key-b-shared"),
                Scope = ResourceScope.Tenant, TenantId = _tenantB
            });
        ctx.SaveChangesAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        AmbientTenant.Value = null;
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
                ["OpenAI"] = new()
                {
                    Name = "OpenAI", Enabled = true, ApiKey = "config-key", DefaultModel = "gpt-4o",
                    BaseUrl = "https://config.example/v1"
                },
                ["Shared"] = new() { Name = "Shared", Enabled = true, ApiKey = "config-shared", DefaultModel = "gpt-4o" },
                // 配置里的主提供商，降级到 OpenAI —— 而 OpenAI 这个名字租户 A 有自己的行
                ["Primary"] = new()
                {
                    Name = "Primary", Enabled = true, ApiKey = "config-primary", DefaultModel = "gpt-4o",
                    BaseUrl = "https://primary.example/v1", FallbackProviders = ["OpenAI"]
                }
            }
        };
        return new ChatClientFactory(
            new StaticOptionsMonitor<AIOptions>(options),
            [_provider],
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _log);
    }

    [Fact]
    public void TenantRow_DoesNotLeakIntoOtherTenant()
    {
        var factory = CreateFactory();

        // A resolves first and gets its own row
        AmbientTenant.Value = _tenantA;
        factory.GetChatClient("OpenAI");
        _provider.LastOptions!.BaseUrl.ShouldBe("https://tenant-a.example/v1");
        _provider.LastOptions.ApiKey.ShouldBe("key-a");

        // B has no row named OpenAI: it must get the configuration entry, not A's cached row
        AmbientTenant.Value = _tenantB;
        factory.GetChatClient("OpenAI");
        _provider.LastOptions!.BaseUrl.ShouldBe("https://config.example/v1");
        _provider.LastOptions.ApiKey.ShouldBe("config-key");
    }

    [Fact]
    public void SystemRow_VisibleToEveryTenant_TenantRowWinsForItsOwner()
    {
        var factory = CreateFactory();

        AmbientTenant.Value = _tenantA;
        factory.GetChatClient("Shared");
        _provider.LastOptions!.BaseUrl.ShouldBe("https://shared.example/v1");

        // B has its own row named Shared: tenant-first, even though the System row has a higher priority
        AmbientTenant.Value = _tenantB;
        factory.GetChatClient("Shared");
        _provider.LastOptions!.BaseUrl.ShouldBe("https://tenant-b-shared.example/v1");
        _provider.LastOptions.ApiKey.ShouldBe("key-b-shared");
    }

    [Fact]
    public void NoTenantContext_NeverPicksUpATenantRow()
    {
        var factory = CreateFactory();

        // host-level callers (IAiUtility, background jobs) resolve with no tenant: System rows only
        AmbientTenant.Value = null;
        factory.GetChatClient("OpenAI");
        _provider.LastOptions!.BaseUrl.ShouldBe("https://config.example/v1");

        factory.GetChatClient("Shared");
        _provider.LastOptions!.BaseUrl.ShouldBe("https://shared.example/v1");
    }

    [Fact]
    public void ChatClientCache_IsPartitionedByTenant()
    {
        var factory = CreateFactory();

        AmbientTenant.Value = _tenantA;
        var clientA = factory.GetChatClient("OpenAI");
        AmbientTenant.Value = _tenantB;
        var clientB = factory.GetChatClient("OpenAI");
        AmbientTenant.Value = _tenantA;
        var clientA2 = factory.GetChatClient("OpenAI");

        clientA.ShouldNotBeSameAs(clientB);
        clientA2.ShouldBeSameAs(clientA);
        _provider.CreateCount.ShouldBe(2);
    }

    [Fact]
    public async Task InvalidateProvider_DropsEveryTenantBucketForThatName()
    {
        var factory = CreateFactory();

        AmbientTenant.Value = _tenantA;
        var before = factory.GetChatClient("OpenAI");

        // admin edits the row: the next resolution must re-read the database instead of the 60s cache
        using (var scope = _serviceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SharedResourceMtDbContext>();
            var row = ctx.Set<Provider>().Single(p => p.Name == "OpenAI");
            row.Endpoint = "https://tenant-a-edited.example/v1";
            await ctx.SaveChangesAsync();
        }

        factory.InvalidateProvider("OpenAI");

        var after = factory.GetChatClient("OpenAI");
        after.ShouldNotBeSameAs(before);
        _provider.LastOptions!.BaseUrl.ShouldBe("https://tenant-a-edited.example/v1");
    }

    /// <summary>
    /// 降级链是跟着主提供商缓存的：主提供商来自配置（分区 null），降级项却按调用方租户解析。
    /// 不把降级项的分区并进缓存键，租户 A 先跑就把「用 A 的密钥建的降级客户端」留给所有人。
    /// </summary>
    [Fact]
    public void FallbackChain_TenantRowInFallback_DoesNotLeakIntoOtherTenants()
    {
        var factory = CreateFactory();

        // A first: primary from config, fallback resolves A's own OpenAI row
        AmbientTenant.Value = _tenantA;
        var chainA = factory.GetChatClient("Primary");
        _provider.AllOptions.Select(o => o.BaseUrl).ShouldBe(["https://primary.example/v1", "https://tenant-a.example/v1"]);

        // B has no OpenAI row: the chain must be rebuilt with the configuration entry, not A's cached one
        AmbientTenant.Value = _tenantB;
        var chainB = factory.GetChatClient("Primary");
        chainB.ShouldNotBeSameAs(chainA);
        _provider.AllOptions.Skip(2).Select(o => o.BaseUrl).ShouldBe(["https://primary.example/v1", "https://config.example/v1"]);
        _provider.AllOptions.Skip(2).Select(o => o.ApiKey).ShouldNotContain("key-a");

        // host-level caller (no tenant) shares B's shape, never A's credentials
        AmbientTenant.Value = null;
        var chainHost = factory.GetChatClient("Primary");
        chainHost.ShouldNotBeSameAs(chainA);
        _provider.AllOptions.Select(o => o.ApiKey).Count(k => k == "key-a").ShouldBe(1);

        // A again: its own chain is still cached
        AmbientTenant.Value = _tenantA;
        factory.GetChatClient("Primary").ShouldBeSameAs(chainA);
    }

    [Fact]
    public void FallbackChain_HostWarmsFirst_TenantStillGetsItsOwnFallbackRow()
    {
        var factory = CreateFactory();

        AmbientTenant.Value = null;
        var chainHost = factory.GetChatClient("Primary");
        _provider.AllOptions.Select(o => o.BaseUrl).ShouldBe(["https://primary.example/v1", "https://config.example/v1"]);

        AmbientTenant.Value = _tenantA;
        var chainA = factory.GetChatClient("Primary");
        chainA.ShouldNotBeSameAs(chainHost);
        _provider.AllOptions.Skip(2).Select(o => o.BaseUrl).ShouldBe(["https://primary.example/v1", "https://tenant-a.example/v1"]);
    }

    // ---------------------------------------------------------------------
    // stubs
    // ---------------------------------------------------------------------

    private sealed class RecordingChatClientProvider : IChatClientProvider
    {
        public string ProviderName => "OpenAI";
        public ProviderOptions? LastOptions { get; private set; }
        public List<ProviderOptions> AllOptions { get; } = [];
        public int CreateCount => AllOptions.Count;

        public IChatClient CreateChatClient(ProviderOptions options, string model)
        {
            LastOptions = options;
            AllOptions.Add(options);
            return Mock.Of<IChatClient>();
        }

        public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(ProviderOptions options, string model) => null;

        public object CreateNativeClient(ProviderOptions options) => new object();
    }

    /// <summary>工厂把库读失败吞成 Warning 后退回配置；测试失败时要能看见那条原因。</summary>
    private sealed class CapturingLogger : ILogger<ChatClientFactory>
    {
        public List<string> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add($"[{logLevel}] {formatter(state, exception)} {exception}");
            if (logLevel >= LogLevel.Warning)
                throw new InvalidOperationException($"ChatClientFactory logged {logLevel}: {formatter(state, exception)}", exception);
        }
    }

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid? tenantId) { Id = tenantId; }
        public Guid? Id { get; }
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
