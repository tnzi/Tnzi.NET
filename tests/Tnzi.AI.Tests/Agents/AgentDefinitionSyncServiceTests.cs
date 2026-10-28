using Microsoft.Data.Sqlite;

using Tnzi.AI.Agents.Definitions;

namespace Tnzi.AI.Tests.Agents;

/// <summary>
/// <c>AI:AgentDefinitions</c> 子系统的集成测试：YAML → 数据库同步，以及文件变更后的热重载。
/// 真实 SQLite + EFCoreRepository + 真实 YamlAgentDefinitionProvider，临时目录写 yaml。
/// </summary>
/// <remarks>
/// 2026-09-12 前 <c>WatchForChanges</c>「热重载」只清提供者的解析缓存、没人重读：
/// 日志写着 cache invalidated，DB 里的 Agent 行与运行时行为直到重启才变；整个子系统零测试零文档。
/// <para>
/// ★ 每个 DbContext 用自己的连接（临时文件库）。后台同步与测试线程的轮询是并发的，此前两者共用一个
/// <see cref="SqliteConnection"/>：同步写库撞上轮询的读，报 <c>database is locked</c>，热重载用例约十次红一次。
/// </para>
/// </remarks>
public class AgentDefinitionSyncServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tnzi-agent-defs-" + Guid.NewGuid().ToString("N"));
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "tnzi-agent-defs-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly ServiceProvider _provider;
    private readonly Mock<IAgentGrantService> _grants = new();
    private readonly List<(Guid AgentId, IReadOnlyList<string>? Groups)> _reconciled = [];

    public AgentDefinitionSyncServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _grants.Setup(g => g.ReconcileToolGroupsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<string>?, CancellationToken>((id, groups, _) => _reconciled.Add((id, groups)))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentUser>(_ =>
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(Guid.Empty);
            return user.Object;
        });
        services.AddDbContext<AgentDefinitionSyncDbContext>(o => o.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IRepository<Agent, Guid>, EFCoreRepository<AgentDefinitionSyncDbContext, Agent, Guid>>();
        services.AddSingleton(_grants.Object);
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentDefinitionSyncDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
        try { File.Delete(_databasePath); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private IOptionsMonitor<AIOptions> Options(bool watch = false, bool syncOnStartup = true) => new StaticOptionsMonitor<AIOptions>(new AIOptions
    {
        AgentDefinitions = new AgentDefinitionOptions { Enabled = true, DirectoryPath = _directory, WatchForChanges = watch, SyncOnStartup = syncOnStartup }
    });

    private void WriteYaml(string file, string name, string instructions, string toolGroups = "[file]")
        => File.WriteAllText(Path.Combine(_directory, file), $"name: {name}\ninstructions: \"{instructions}\"\nprovider: OpenAI\nmodel: gpt-4o\ntoolGroups: {toolGroups}\n");

    private async Task<List<Agent>> AgentsAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AgentDefinitionSyncDbContext>().Set<Agent>().AsNoTracking().ToListAsync();
    }

    private (AgentDefinitionSyncService service, YamlAgentDefinitionProvider provider) Build(IOptionsMonitor<AIOptions> options)
    {
        var yaml = new YamlAgentDefinitionProvider(options, NullLogger<YamlAgentDefinitionProvider>.Instance);
        var service = new AgentDefinitionSyncService(_provider, yaml, options, NullLogger<AgentDefinitionSyncService>.Instance)
        {
            ChangeDebounce = TimeSpan.FromMilliseconds(100)
        };
        return (service, yaml);
    }

    [Fact]
    public async Task StartAsync_CreatesAgentsFromYaml_AndReconcilesToolGroups()
    {
        WriteYaml("reviewer.yaml", "reviewer", "Review code", "[file, code]");
        var (service, _) = Build(Options());

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        var agent = (await AgentsAsync()).ShouldHaveSingleItem();
        agent.Name.ShouldBe("reviewer");
        agent.Source.ShouldBe(AgentSources.Yaml);
        agent.Instructions.ShouldBe("Review code");
        agent.DefinitionHash.ShouldNotBeNullOrEmpty();
        _reconciled.ShouldHaveSingleItem().Groups.ShouldBe(["file", "code"]);
    }

    [Fact]
    public async Task SyncAllAsync_SameHash_SkipsUpdate_ChangedHash_Updates()
    {
        WriteYaml("reviewer.yaml", "reviewer", "v1");
        var (service, _) = Build(Options());
        await service.StartAsync(CancellationToken.None);
        var first = (await AgentsAsync()).Single();

        var unchanged = await service.SyncAllAsync(CancellationToken.None);
        unchanged.Synced.ShouldBe(0);
        unchanged.Skipped.ShouldBe(1);
        (await AgentsAsync()).Single().DefinitionHash.ShouldBe(first.DefinitionHash);

        WriteYaml("reviewer.yaml", "reviewer", "v2");
        var changed = await service.SyncAllAsync(CancellationToken.None);
        changed.Synced.ShouldBe(1);
        var updated = (await AgentsAsync()).Single();
        updated.Id.ShouldBe(first.Id);
        updated.Instructions.ShouldBe("v2");
        updated.DefinitionHash.ShouldNotBe(first.DefinitionHash);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SyncAllAsync_DatabaseSourceAgent_IsNotOverwritten()
    {
        using (var scope = _provider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AgentDefinitionSyncDbContext>();
            ctx.Set<Agent>().Add(new Agent { Name = "reviewer", Provider = "OpenAI", Instructions = "api-managed", Source = AgentSources.Database });
            await ctx.SaveChangesAsync();
        }
        WriteYaml("reviewer.yaml", "reviewer", "from yaml");
        var (service, _) = Build(Options());

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        var agent = (await AgentsAsync()).ShouldHaveSingleItem();
        agent.Instructions.ShouldBe("api-managed");
        agent.Source.ShouldBe(AgentSources.Database);
        _reconciled.ShouldBeEmpty();
    }

    [Fact]
    public async Task FileChanged_ResyncsAfterDebounce()
    {
        WriteYaml("reviewer.yaml", "reviewer", "v1");
        var (service, _) = Build(Options(watch: true));
        await service.StartAsync(CancellationToken.None);
        (await AgentsAsync()).Single().Instructions.ShouldBe("v1");

        WriteYaml("reviewer.yaml", "reviewer", "v2");
        WriteYaml("writer.yaml", "writer", "Write things");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        List<Agent> agents;
        do
        {
            await Task.Delay(100);
            agents = await AgentsAsync();
        } while (DateTime.UtcNow < deadline && !(agents.Count == 2 && agents.Any(a => a.Instructions == "v2")));

        await service.StopAsync(CancellationToken.None);
        agents.Count.ShouldBe(2);
        agents.Single(a => a.Name == "reviewer").Instructions.ShouldBe("v2");
    }

    /// <summary>
    /// 一次失败的重新同步自己会重试：此前它只记一条错误，改动要等到下一次存盘（或重启）才进数据库。
    /// </summary>
    [Fact]
    public async Task FailedResync_IsRetried_WithoutAnotherFileChange()
    {
        var definitions = new FlakyDefinitionProvider(failures: 1);
        var options = Options(watch: true, syncOnStartup: false);
        var service = new AgentDefinitionSyncService(_provider, definitions, options, NullLogger<AgentDefinitionSyncService>.Instance)
        {
            ChangeDebounce = TimeSpan.FromMilliseconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(50),
        };
        await service.StartAsync(CancellationToken.None);

        definitions.RaiseChanged();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        List<Agent> agents;
        do
        {
            await Task.Delay(50);
            agents = await AgentsAsync();
        } while (DateTime.UtcNow < deadline && agents.Count == 0);

        await service.StopAsync(CancellationToken.None);
        agents.ShouldHaveSingleItem().Name.ShouldBe("reviewer");
        definitions.Loads.ShouldBeGreaterThanOrEqualTo(2);
    }

    /// <summary>第一次（或前 <c>failures</c> 次）读定义时抛，之后正常返回一条定义；变更由测试手动触发。</summary>
    private sealed class FlakyDefinitionProvider(int failures) : IAgentDefinitionProvider
    {
        private Action? _changed;
        private int _loads;

        public int Loads => Volatile.Read(ref _loads);

        public void RaiseChanged() => _changed?.Invoke();

        public Task<IReadOnlyList<AgentDefinitionDto>> LoadDefinitionsAsync(CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _loads) <= failures)
            {
                throw new InvalidOperationException("transient failure");
            }

            IReadOnlyList<AgentDefinitionDto> result =
            [
                new AgentDefinitionDto { Name = "reviewer", Instructions = "v1", Provider = "OpenAI", Model = "gpt-4o", DefinitionHash = "h1" },
            ];
            return Task.FromResult(result);
        }

        public Task<AgentDefinitionDto?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<AgentDefinitionDto?>(null);

        public IDisposable OnDefinitionsChanged(Action callback)
        {
            _changed = callback;
            return new Unsubscribe(() => _changed = null);
        }

        private sealed class Unsubscribe(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }

    [Fact]
    public async Task StartAsync_SyncOnStartupDisabled_DoesNotSync()
    {
        WriteYaml("reviewer.yaml", "reviewer", "v1");
        var (service, _) = Build(Options(syncOnStartup: false));

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        (await AgentsAsync()).ShouldBeEmpty();
    }
}

internal sealed class AgentDefinitionSyncDbContext : TnziDbContext<AgentDefinitionSyncDbContext>
{
    public AgentDefinitionSyncDbContext(DbContextOptions<AgentDefinitionSyncDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
