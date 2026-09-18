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
/// </remarks>
public class AgentDefinitionSyncServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tnzi-agent-defs-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly Mock<IAgentGrantService> _grants = new();
    private readonly List<(Guid AgentId, IReadOnlyList<string>? Groups)> _reconciled = [];

    public AgentDefinitionSyncServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

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
        services.AddDbContext<AgentDefinitionSyncDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<IRepository<Agent, Guid>, EFCoreRepository<AgentDefinitionSyncDbContext, Agent, Guid>>();
        services.AddSingleton(_grants.Object);
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentDefinitionSyncDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
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
