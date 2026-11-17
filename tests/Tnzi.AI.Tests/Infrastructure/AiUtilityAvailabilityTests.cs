using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Tnzi.AI.Tests.Integration;
using Tnzi.AI.Tests.Services;
using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// <see cref="AiUtilityService.IsAvailable"/> answers for the provider a call without a caller-supplied provider would use:
/// <c>AI:DefaultProvider</c> resolved through <see cref="ChatClientFactory"/>, database rows included.
/// </summary>
/// <remarks>
/// A deployment whose only provider is an admin-registered <see cref="Provider"/> row used to read as unavailable,
/// and thread titles (gated on it) were never generated there, with no error anywhere.
/// Real SQLite + EFCoreRepository + real ChatClientFactory; only the SDK client is a stub.
/// </remarks>
public class AiUtilityAvailabilityTests : IDisposable
{
    private const string DatabaseOnly = "acme-llm";
    private const string Disabled = "retired-llm";
    private const string TenantOnly = "tenant-llm";

    private static readonly AsyncLocal<Guid?> AmbientTenant = new();

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();

    public AiUtilityAvailabilityTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentUser>(_ => Mock.Of<ICurrentUser>());
        services.AddScoped<ICurrentTenant>(_ => new StubCurrentTenant(AmbientTenant.Value));
        services.AddSingleton(MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
        services.AddDbContext<SharedResourceMtDbContext>(o => o
            .UseSqlite(_connection)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>());
        services.AddScoped<IRepository<Provider, Guid>, EFCoreRepository<SharedResourceMtDbContext, Provider, Guid>>();
        services.AddSingleton(_dataProtection);
        _serviceProvider = services.BuildServiceProvider();

        var protector = _dataProtection.CreateProtector(Provider.ApiKeyProtectorPurpose);
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SharedResourceMtDbContext>();
        context.Database.EnsureCreated();
        context.Set<Provider>().AddRange(
            new Provider
            {
                Name = DatabaseOnly, ProviderType = "OpenAI", IsEnabled = true, DefaultModel = "acme-1",
                Endpoint = "https://llm.acme.example/v1", ApiKeyEncrypted = protector.Protect("db-key"),
                Scope = ResourceScope.System
            },
            new Provider
            {
                Name = Disabled, ProviderType = "OpenAI", IsEnabled = false, DefaultModel = "old-1",
                ApiKeyEncrypted = protector.Protect("old-key"), Scope = ResourceScope.System
            },
            new Provider
            {
                Name = TenantOnly, ProviderType = "OpenAI", IsEnabled = true, DefaultModel = "t-1",
                Endpoint = "https://tenant.example/v1", ApiKeyEncrypted = protector.Protect("tenant-key"),
                Scope = ResourceScope.Tenant, TenantId = _tenant
            });
        context.SaveChangesAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        AmbientTenant.Value = null;
        _serviceProvider.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static AIOptions Options(string? defaultProvider, params (string Name, bool Enabled)[] configured) => new()
    {
        DefaultProvider = defaultProvider ?? string.Empty,
        Providers = configured.ToDictionary(
            p => p.Name,
            p => new ProviderOptions { Name = p.Name, Enabled = p.Enabled, ApiKey = "config-key", DefaultModel = "gpt-4o" })
    };

    private ChatClientFactory CreateFactory(AIOptions options, IServiceScopeFactory? scopes = null)
        => new(
            new StaticOptionsMonitor<AIOptions>(options),
            [new AnsweringChatClientProvider("unused")],
            scopes ?? _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChatClientFactory>.Instance);

    private static AiUtilityService CreateUtility(IChatClientFactory factory, AIOptions options)
        => new(
            factory,
            new StaticOptionsMonitor<AiUtilityOptions>(new AiUtilityOptions()),
            aiOptions: new StaticOptionsMonitor<AIOptions>(options));

    private AiUtilityService CreateUtility(AIOptions options) => CreateUtility(CreateFactory(options), options);

    // ---------------------------------------------------------------- available

    [Fact]
    public void DefaultProviderOnlyInTheDatabase_IsAvailable()
    {
        CreateUtility(Options(DatabaseOnly)).IsAvailable.ShouldBeTrue(
            "an enabled System row is what GetChatClient would use, with no AI:Providers section at all");
    }

    [Fact]
    public void DefaultProviderConfigured_IsAvailable_AsBefore()
    {
        CreateUtility(Options("OpenAI", ("OpenAI", true))).IsAvailable.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- unavailable

    [Fact]
    public void NoProviderAnywhere_IsUnavailable()
    {
        CreateUtility(Options(defaultProvider: null)).IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void DefaultProviderIsADisabledRow_IsUnavailable()
    {
        CreateUtility(Options(Disabled)).IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void DefaultProviderDisabledInConfiguration_IsUnavailable()
    {
        CreateUtility(Options("OpenAI", ("OpenAI", false))).IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void DefaultNameUnresolved_IsUnavailable_EvenWithOtherProvidersConfigured()
    {
        CreateUtility(Options("nowhere", ("OpenAI", true))).IsAvailable.ShouldBeFalse(
            "a call without a provider resolves the default name, so another configured provider does not make it work");
    }

    // ---------------------------------------------------------------- tenants

    [Fact]
    public void TenantRow_CountsForThatTenant_ButNotForAHostLevelCaller()
    {
        var utility = CreateUtility(Options(TenantOnly));

        AmbientTenant.Value = _tenant;
        utility.IsAvailable.ShouldBeTrue();

        AmbientTenant.Value = null;
        utility.IsAvailable.ShouldBeFalse(
            "without a tenant context only System rows are considered, exactly as GetChatClient resolves them");

        AmbientTenant.Value = Guid.NewGuid();
        utility.IsAvailable.ShouldBeFalse("another tenant's row is not this tenant's provider");
    }

    // ---------------------------------------------------------------- an admin adds the provider

    [Fact]
    public async Task ProviderRegisteredAfterAMiss_CountsOnceTheFactoryIsInvalidated()
    {
        const string added = "added-later";
        var options = Options(added);
        var factory = CreateFactory(options);
        var utility = CreateUtility(factory, options);

        utility.IsAvailable.ShouldBeFalse("the miss is cached for the TTL");

        using (var scope = _serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SharedResourceMtDbContext>();
            context.Set<Provider>().Add(new Provider
            {
                Name = added, ProviderType = "OpenAI", IsEnabled = true, DefaultModel = "new-1", Scope = ResourceScope.System
            });
            await context.SaveChangesAsync();
        }

        utility.IsAvailable.ShouldBeFalse("still the cached miss: the factory has not been told");

        factory.InvalidateProvider(added); // what ProviderService does after every create / update / delete

        utility.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task ProviderDisabledByAnAdmin_StopsCountingOnceTheFactoryIsInvalidated()
    {
        var options = Options(DatabaseOnly);
        var factory = CreateFactory(options);
        var utility = CreateUtility(factory, options);
        utility.IsAvailable.ShouldBeTrue();

        using (var scope = _serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SharedResourceMtDbContext>();
            var row = await context.Set<Provider>().SingleAsync(p => p.Name == DatabaseOnly);
            row.IsEnabled = false;
            await context.SaveChangesAsync();
        }

        factory.InvalidateProvider(DatabaseOnly);

        utility.IsAvailable.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- failures read as unavailable

    [Fact]
    public void DatabaseFailure_FallsBackToConfiguration_AndNeverThrows()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRepository<Provider, Guid>>(_ => throw new InvalidDataException("database is down"));
        using var broken = services.BuildServiceProvider();
        var scopes = broken.GetRequiredService<IServiceScopeFactory>();

        var databaseOnly = Options(DatabaseOnly);
        CreateUtility(CreateFactory(databaseOnly, scopes), databaseOnly).IsAvailable.ShouldBeFalse();

        var configured = Options("OpenAI", ("OpenAI", true));
        CreateUtility(CreateFactory(configured, scopes), configured).IsAvailable.ShouldBeTrue(
            "the factory falls back to configuration when the database cannot be read");
    }

    [Fact]
    public void UnexpectedExceptionFromTheFactory_ReadsAsUnavailable()
    {
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.IsProviderAvailable(It.IsAny<string>())).Throws(new ObjectDisposedException("IServiceProvider"));

        CreateUtility(factory.Object, Options("OpenAI", ("OpenAI", true))).IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void AsksForTheDefaultProviderByName()
    {
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.IsProviderAvailable(DatabaseOnly)).Returns(true);

        CreateUtility(factory.Object, Options(DatabaseOnly)).IsAvailable.ShouldBeTrue();
        factory.Verify(f => f.GetAvailableProviders(), Times.Never, "the configured list is not the question");
    }

    private sealed class StubCurrentTenant(Guid? tenantId) : ICurrentTenant
    {
        public Guid? Id { get; } = tenantId;
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}

/// <summary>
/// End to end on a deployment whose only provider is a database row: the first message asks for an AI title,
/// and the background step writes it.
/// </summary>
public class ThreadTitleWithDatabaseProviderTests : IntegratedTestBase<ThreadAttachmentDbContext>
{
    private const string DatabaseOnly = "acme-llm";

    private readonly List<ThreadTitleRequestedEvent> _published = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<AgentThreadEntity, Guid>, EFCoreRepository<ThreadAttachmentDbContext, AgentThreadEntity, Guid>>();
        services.AddScoped<IRepository<Provider, Guid>, EFCoreRepository<ThreadAttachmentDbContext, Provider, Guid>>();

        // No AI:Providers section at all: the only provider is the row seeded below
        var aiOptions = new StaticOptionsMonitor<AIOptions>(new AIOptions { DefaultProvider = DatabaseOnly });
        services.AddSingleton<IChatClientFactory>(sp => new ChatClientFactory(
            aiOptions,
            [new AnsweringChatClientProvider("Kyoto Spring Trip")],
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChatClientFactory>.Instance));
        services.AddSingleton<IAiUtility>(sp => new AiUtilityService(
            sp.GetRequiredService<IChatClientFactory>(),
            new StaticOptionsMonitor<AiUtilityOptions>(new AiUtilityOptions()),
            aiOptions: aiOptions));

        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<ThreadTitleRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ThreadTitleRequestedEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);

        services.AddScoped<IThreadTitleService>(sp => new ThreadTitleService(
            sp.GetRequiredService<IRepository<AgentThreadEntity, Guid>>(),
            new StaticOptionsMonitor<ThreadOptions>(new ThreadOptions { AutoGenerateTitle = true }),
            NullLogger<ThreadTitleService>.Instance,
            bus.Object,
            sp.GetRequiredService<IAiUtility>()));
    }

    [Fact]
    public async Task FirstMessage_RequestsAnAiTitle_AndTheBackgroundStepWritesIt()
    {
        DbContext.Set<Provider>().Add(new Provider
        {
            Name = DatabaseOnly, ProviderType = "OpenAI", IsEnabled = true, DefaultModel = "acme-1", Scope = ResourceScope.System
        });
        var thread = new AgentThreadEntity { Title = "Thread 2026-11-04 09:00", LastActivityTime = DateTime.UtcNow };
        DbContext.Set<AgentThreadEntity>().Add(thread);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using (var scope = ServiceProvider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IThreadTitleService>()
                .StartAsync(new ThreadTitleRequest(thread.Id, "Please help me plan a three day trip to Kyoto in spring", null, null));
        }

        (await ReadAsync(thread.Id)).TitleSource.ShouldBe(ThreadTitleSource.Generating,
            "IsAvailable is true for the database provider, so an AI title is on its way");
        var requested = _published.ShouldHaveSingleItem();

        using (var scope = ServiceProvider.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IThreadTitleService>().GenerateAsync(requested)).ShouldBeTrue();
        }

        var titled = await ReadAsync(thread.Id);
        titled.Title.ShouldBe("Kyoto Spring Trip");
        titled.TitleSource.ShouldBe(ThreadTitleSource.Generated);
    }

    [Fact]
    public async Task NoUsableProvider_WritesOnlyTheFallback()
    {
        var thread = new AgentThreadEntity { Title = "Thread 2026-11-04 09:00", LastActivityTime = DateTime.UtcNow };
        DbContext.Set<AgentThreadEntity>().Add(thread);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using (var scope = ServiceProvider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IThreadTitleService>()
                .StartAsync(new ThreadTitleRequest(thread.Id, "Please help me plan a three day trip to Kyoto in spring", null, null));
        }

        (await ReadAsync(thread.Id)).TitleSource.ShouldBe(ThreadTitleSource.Fallback,
            "no row and no configuration: the client is told no AI title is coming");
        _published.ShouldBeEmpty();
    }

    private async Task<AgentThreadEntity> ReadAsync(Guid threadId)
        => await DbContext.Set<AgentThreadEntity>().AsNoTracking().SingleAsync(t => t.Id == threadId);
}

/// <summary>An OpenAI-protocol provider whose clients answer every request with one fixed text.</summary>
internal sealed class AnsweringChatClientProvider(string answer) : IChatClientProvider
{
    public string ProviderName => "OpenAI";

    public IChatClient CreateChatClient(ProviderOptions options, string model)
    {
        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { FinishReason = ChatFinishReason.Stop });
        return client.Object;
    }

    public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(ProviderOptions options, string model) => null;
}
