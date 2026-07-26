using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests;

/// <summary>
/// IAgentGrantService 集成测试 - 使用 SQLite 内存库 + 真实 EFCoreRepository（镜像 AgentProviderFkTests 的
/// 自包含模式），因为 reconcile 的 diff 语义、软删除过滤、metadata 保留必须经过真实的 EF Core 查询/写入管道。
/// </summary>
public class AgentGrantServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AgentGrantDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly AgentGrantService _service;

    public AgentGrantServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Mapster 默认配置（ApplicationService 基类间接需要）
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);

        var options = new DbContextOptionsBuilder<AgentGrantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AgentGrantDbContext(options, currentUserMock.Object);
        _context.Database.EnsureCreated();

        _serviceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();
        _service = new AgentGrantService(
            _serviceProvider,
            new EFCoreRepository<AgentGrantDbContext, AgentToolGrant, Guid>(_context),
            new EFCoreRepository<AgentGrantDbContext, AgentSkillGrant, Guid>(_context),
            new EFCoreRepository<AgentGrantDbContext, AgentKnowledgeGrant, Guid>(_context));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Guid> SeedAgentAsync(string name = "GrantAgent")
    {
        var agent = new Agent { Name = name, Provider = "MockProvider" };
        _context.Set<Agent>().Add(agent);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return agent.Id;
    }

    // =====================================================================
    // 1. Reconcile null = no-op
    // =====================================================================

    [Fact]
    public async Task ReconcileToolGroups_Null_IsNoOp()
    {
        var agentId = await SeedAgentAsync();
        await _service.ReconcileToolGroupsAsync(agentId, new[] { "fs", "shell" });
        _context.ChangeTracker.Clear();

        // null input → 不应改变任何现有授权
        await _service.ReconcileToolGroupsAsync(agentId, null);

        var groups = (await _service.GetGrantsAsync(agentId)).ToolGroups;
        groups.ShouldBe(new[] { "fs", "shell" }, ignoreOrder: true);
    }

    // =====================================================================
    // 2. Reconcile [] = clears all
    // =====================================================================

    [Fact]
    public async Task ReconcileToolGroups_Empty_ClearsAll()
    {
        var agentId = await SeedAgentAsync();
        await _service.ReconcileToolGroupsAsync(agentId, new[] { "fs", "shell" });
        _context.ChangeTracker.Clear();

        await _service.ReconcileToolGroupsAsync(agentId, Array.Empty<string>());

        var groups = (await _service.GetGrantsAsync(agentId)).ToolGroups;
        groups.ShouldBeEmpty();
    }

    // =====================================================================
    // 3. Reconcile populated = adds new + removes absent + PRESERVES existing metadata
    // =====================================================================

    [Fact]
    public async Task ReconcileToolGroups_Populated_AddsRemovesAndPreservesExistingMetadata()
    {
        var agentId = await SeedAgentAsync();
        await _service.ReconcileToolGroupsAsync(agentId, new[] { "fs", "shell" });
        _context.ChangeTracker.Clear();

        // 给 "fs" 设置 admin 自定义的 Priority=5 + IsEnabled（直接经 DB 模拟治理面操作）
        var fsGrant = await _context.Set<AgentToolGrant>()
            .FirstAsync(g => g.AgentId == agentId && g.ToolKey == "fs");
        fsGrant.Priority = 5;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // reconcile：保留 fs、删除 shell、新增 git
        await _service.ReconcileToolGroupsAsync(agentId, new[] { "fs", "git" });
        _context.ChangeTracker.Clear();

        var grants = await _context.Set<AgentToolGrant>()
            .Where(g => g.AgentId == agentId)
            .ToListAsync();

        var keys = grants.Select(g => g.ToolKey).ToList();
        keys.ShouldBe(new[] { "fs", "git" }, ignoreOrder: true);

        // 关键断言：保留下来的 fs 仍带 admin 设置的 Priority=5（未被 delete+reinsert 抹掉）
        var preserved = grants.Single(g => g.ToolKey == "fs");
        preserved.Priority.ShouldBe(5);
        preserved.IsEnabled.ShouldBeTrue();
    }

    // =====================================================================
    // 4. GetGrantsAsync projects only enabled grants ordered by Priority DESC
    // =====================================================================

    [Fact]
    public async Task GetGrants_ProjectsOnlyEnabled_OrderedByPriorityDesc()
    {
        var agentId = await SeedAgentAsync();

        // 直接写入三条工具组授权：low(P=1, enabled) / high(P=10, enabled) / disabled(P=99, disabled)
        _context.Set<AgentToolGrant>().AddRange(
            new AgentToolGrant { AgentId = agentId, GrantType = GrantType.Group, ToolKey = "low", Priority = 1, IsEnabled = true },
            new AgentToolGrant { AgentId = agentId, GrantType = GrantType.Group, ToolKey = "high", Priority = 10, IsEnabled = true },
            new AgentToolGrant { AgentId = agentId, GrantType = GrantType.Group, ToolKey = "disabled", Priority = 99, IsEnabled = false });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var projection = await _service.GetGrantsAsync(agentId);

        // 仅启用条目 + 按 Priority 降序（high 在前）
        projection.ToolGroups.ShouldBe(new[] { "high", "low" });
    }

    [Fact]
    public async Task GetGrants_SeparatesToolGroupsFromToolNames()
    {
        var agentId = await SeedAgentAsync();

        _context.Set<AgentToolGrant>().AddRange(
            new AgentToolGrant { AgentId = agentId, GrantType = GrantType.Group, ToolKey = "fs", IsEnabled = true },
            new AgentToolGrant { AgentId = agentId, GrantType = GrantType.Tool, ToolKey = "read_file", IsEnabled = true });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var projection = await _service.GetGrantsAsync(agentId);

        projection.ToolGroups.ShouldBe(new[] { "fs" });
        projection.ToolNames.ShouldBe(new[] { "read_file" });
    }

    // =====================================================================
    // 5. Reverse query returns the right agent ids
    // =====================================================================

    [Fact]
    public async Task GetAgentsUsingTool_ReturnsDistinctEnabledAgents()
    {
        var agentA = await SeedAgentAsync("A");
        var agentB = await SeedAgentAsync("B");
        var agentC = await SeedAgentAsync("C");

        _context.Set<AgentToolGrant>().AddRange(
            new AgentToolGrant { AgentId = agentA, GrantType = GrantType.Group, ToolKey = "fs", IsEnabled = true },
            new AgentToolGrant { AgentId = agentB, GrantType = GrantType.Tool, ToolKey = "fs", IsEnabled = true },
            new AgentToolGrant { AgentId = agentC, GrantType = GrantType.Group, ToolKey = "fs", IsEnabled = false }); // disabled → excluded
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var agents = await _service.GetAgentsUsingToolAsync("fs");

        agents.ShouldBe(new[] { agentA, agentB }, ignoreOrder: true);
    }

    [Fact]
    public async Task GetAgentsUsingKnowledge_ReturnsMatchingAgents()
    {
        var agentA = await SeedAgentAsync("A");
        var agentB = await SeedAgentAsync("B");
        var kbId = Guid.NewGuid();

        await _service.ReconcileKnowledgeAsync(agentA, new[] { kbId });
        await _service.ReconcileKnowledgeAsync(agentB, new[] { Guid.NewGuid() }); // different KB
        _context.ChangeTracker.Clear();

        var agents = await _service.GetAgentsUsingKnowledgeAsync(kbId);

        agents.ShouldBe(new[] { agentA });
    }

    // =====================================================================
    // 6. Tool-group reconcile does NOT touch GrantType=Tool grants
    // =====================================================================

    [Fact]
    public async Task ReconcileToolGroups_DoesNotTouchSingleToolGrants()
    {
        var agentId = await SeedAgentAsync();

        // 一条单工具授权（GrantType=Tool）
        _context.Set<AgentToolGrant>().Add(
            new AgentToolGrant { AgentId = agentId, GrantType = GrantType.Tool, ToolKey = "read_file", IsEnabled = true });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // reconcile 工具组为空 → 应清空所有 Group 授权，但单工具授权不受影响
        await _service.ReconcileToolGroupsAsync(agentId, Array.Empty<string>());
        _context.ChangeTracker.Clear();

        var toolGrants = await _context.Set<AgentToolGrant>()
            .Where(g => g.AgentId == agentId)
            .ToListAsync();

        // 单工具授权依然存活
        toolGrants.Count.ShouldBe(1);
        toolGrants[0].GrantType.ShouldBe(GrantType.Tool);
        toolGrants[0].ToolKey.ShouldBe("read_file");
    }

    // =====================================================================
    // ReconcileToolNames (GrantType=Tool) - independent of ReconcileToolGroups
    // =====================================================================

    [Fact]
    public async Task ReconcileToolNames_Populated_AddsOnlyToolGrants()
    {
        var agentId = await SeedAgentAsync();

        await _service.ReconcileToolNamesAsync(agentId, new[] { "read_file", "write_file" });
        _context.ChangeTracker.Clear();

        var projection = await _service.GetGrantsAsync(agentId);
        projection.ToolNames.ShouldBe(new[] { "read_file", "write_file" }, ignoreOrder: true);
        projection.ToolGroups.ShouldBeEmpty();

        // Stored as GrantType=Tool only.
        var grants = await _context.Set<AgentToolGrant>().Where(g => g.AgentId == agentId).ToListAsync();
        grants.ShouldAllBe(g => g.GrantType == GrantType.Tool);
    }

    [Fact]
    public async Task ReconcileToolNames_Null_IsNoOp()
    {
        var agentId = await SeedAgentAsync();
        await _service.ReconcileToolNamesAsync(agentId, new[] { "read_file" });
        _context.ChangeTracker.Clear();

        await _service.ReconcileToolNamesAsync(agentId, null);

        (await _service.GetGrantsAsync(agentId)).ToolNames.ShouldBe(new[] { "read_file" });
    }

    [Fact]
    public async Task ReconcileToolNames_Empty_ClearsOnlyToolGrants_NotGroups()
    {
        var agentId = await SeedAgentAsync();
        await _service.ReconcileToolGroupsAsync(agentId, new[] { "fs" });
        await _service.ReconcileToolNamesAsync(agentId, new[] { "read_file" });
        _context.ChangeTracker.Clear();

        // Clearing tool names must NOT touch the tool-group grant.
        await _service.ReconcileToolNamesAsync(agentId, Array.Empty<string>());
        _context.ChangeTracker.Clear();

        var projection = await _service.GetGrantsAsync(agentId);
        projection.ToolNames.ShouldBeEmpty();
        projection.ToolGroups.ShouldBe(new[] { "fs" });
    }

    [Fact]
    public async Task ReconcileToolGroups_DoesNotClobberToolNames()
    {
        var agentId = await SeedAgentAsync();
        await _service.ReconcileToolNamesAsync(agentId, new[] { "read_file" });
        _context.ChangeTracker.Clear();

        // Reconciling groups to a new set must leave the Tool grant untouched.
        await _service.ReconcileToolGroupsAsync(agentId, new[] { "git" });
        _context.ChangeTracker.Clear();

        var projection = await _service.GetGrantsAsync(agentId);
        projection.ToolGroups.ShouldBe(new[] { "git" });
        projection.ToolNames.ShouldBe(new[] { "read_file" });
    }

    [Fact]
    public async Task ReconcileToolNames_DisabledButStillDesired_ReEnablesGrant()
    {
        var agentId = await SeedAgentAsync();
        await _service.ReconcileToolNamesAsync(agentId, new[] { "read_file", "write_file" });
        _context.ChangeTracker.Clear();

        var grant = await _context.Set<AgentToolGrant>()
            .FirstAsync(g => g.AgentId == agentId && g.ToolKey == "read_file" && g.GrantType == GrantType.Tool);
        var grantId = grant.Id;
        grant.IsEnabled = false;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        (await _service.GetGrantsAsync(agentId)).ToolNames.ShouldNotContain("read_file");
        _context.ChangeTracker.Clear();

        await _service.ReconcileToolNamesAsync(agentId, new[] { "read_file", "write_file" });
        _context.ChangeTracker.Clear();

        var names = (await _service.GetGrantsAsync(agentId)).ToolNames;
        names.ShouldBe(new[] { "read_file", "write_file" }, ignoreOrder: true);

        var reEnabled = await _context.Set<AgentToolGrant>()
            .FirstAsync(g => g.AgentId == agentId && g.ToolKey == "read_file" && g.GrantType == GrantType.Tool);
        reEnabled.Id.ShouldBe(grantId); // same row, not delete+reinsert
    }

    // =====================================================================
    // Reconcile de-duplicates input
    // =====================================================================

    [Fact]
    public async Task ReconcileSkills_DeduplicatesInput()
    {
        var agentId = await SeedAgentAsync();

        await _service.ReconcileSkillsAsync(agentId, new[] { "writing", "writing", "research" });
        _context.ChangeTracker.Clear();

        var slugs = (await _service.GetGrantsAsync(agentId)).SkillSlugs;
        slugs.ShouldBe(new[] { "writing", "research" }, ignoreOrder: true);
    }

    // =====================================================================
    // Reconcile RE-ENABLES a disabled-but-still-desired grant (FIX 2)
    // =====================================================================

    [Fact]
    public async Task ReconcileSkills_DisabledButStillDesired_ReEnablesGrant()
    {
        var agentId = await SeedAgentAsync();
        await _service.ReconcileSkillsAsync(agentId, new[] { "writing", "research" });
        _context.ChangeTracker.Clear();

        // 治理面禁用 "writing"（直接经 DB 模拟 SetGrantEnabledAsync 的效果）
        var grantId = await _context.Set<AgentSkillGrant>()
            .Where(g => g.AgentId == agentId && g.SkillSlug == "writing")
            .Select(g => g.Id)
            .FirstAsync();
        _context.ChangeTracker.Clear();
        var ok = await _service.SetGrantEnabledAsync(GrantResourceType.Skill, grantId, enabled: false);
        ok.ShouldBeTrue();
        _context.ChangeTracker.Clear();

        // 禁用后投影里不再包含它
        (await _service.GetGrantsAsync(agentId)).SkillSlugs.ShouldNotContain("writing");
        _context.ChangeTracker.Clear();

        // reconcile 时 "writing" 仍在目标集中 → 必须重新启用（reconcile = "这就是应启用的授权集合"）
        await _service.ReconcileSkillsAsync(agentId, new[] { "writing", "research" });
        _context.ChangeTracker.Clear();

        // 重新启用后回到投影；且未 delete+reinsert（同一行翻转 IsEnabled）
        var slugs = (await _service.GetGrantsAsync(agentId)).SkillSlugs;
        slugs.ShouldBe(new[] { "writing", "research" }, ignoreOrder: true);

        var reEnabled = await _context.Set<AgentSkillGrant>()
            .FirstAsync(g => g.AgentId == agentId && g.SkillSlug == "writing");
        reEnabled.Id.ShouldBe(grantId); // 同一条 grant，未被删除重建
        reEnabled.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task ReconcileToolGroups_DisabledButStillDesired_ReEnablesGrant()
    {
        var agentId = await SeedAgentAsync();
        await _service.ReconcileToolGroupsAsync(agentId, new[] { "fs", "shell" });
        _context.ChangeTracker.Clear();

        // 直接禁用 "fs"
        var fsGrant = await _context.Set<AgentToolGrant>()
            .FirstAsync(g => g.AgentId == agentId && g.ToolKey == "fs");
        fsGrant.IsEnabled = false;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        (await _service.GetGrantsAsync(agentId)).ToolGroups.ShouldNotContain("fs");
        _context.ChangeTracker.Clear();

        // reconcile 仍包含 "fs" → 重新启用
        await _service.ReconcileToolGroupsAsync(agentId, new[] { "fs", "shell" });
        _context.ChangeTracker.Clear();

        var groups = (await _service.GetGrantsAsync(agentId)).ToolGroups;
        groups.ShouldBe(new[] { "fs", "shell" }, ignoreOrder: true);
    }

    // =====================================================================
    // Per-grant CRUD
    // =====================================================================

    [Fact]
    public async Task SetGrantEnabled_TogglesAndAffectsProjection()
    {
        var agentId = await SeedAgentAsync();
        await _service.ReconcileToolGroupsAsync(agentId, new[] { "fs" });
        _context.ChangeTracker.Clear();

        var grantId = await _context.Set<AgentToolGrant>()
            .Where(g => g.AgentId == agentId && g.ToolKey == "fs")
            .Select(g => g.Id)
            .FirstAsync();
        _context.ChangeTracker.Clear();

        var ok = await _service.SetGrantEnabledAsync(GrantResourceType.Tool, grantId, enabled: false);
        ok.ShouldBeTrue();
        _context.ChangeTracker.Clear();

        // disabled → 投影不再包含它
        var groups = (await _service.GetGrantsAsync(agentId)).ToolGroups;
        groups.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetGrantPriority_UpdatesAndReordersProjection()
    {
        var agentId = await SeedAgentAsync();
        await _service.ReconcileToolGroupsAsync(agentId, new[] { "fs", "git" });
        _context.ChangeTracker.Clear();

        var gitId = await _context.Set<AgentToolGrant>()
            .Where(g => g.AgentId == agentId && g.ToolKey == "git")
            .Select(g => g.Id)
            .FirstAsync();
        _context.ChangeTracker.Clear();

        // 抬高 git 优先级 → 它应排到 fs 前面
        var ok = await _service.SetGrantPriorityAsync(GrantResourceType.Tool, gitId, priority: 100);
        ok.ShouldBeTrue();
        _context.ChangeTracker.Clear();

        var groups = (await _service.GetGrantsAsync(agentId)).ToolGroups;
        groups[0].ShouldBe("git");
    }

    [Fact]
    public async Task SetGrantEnabled_MissingGrant_ReturnsFalse()
    {
        var ok = await _service.SetGrantEnabledAsync(GrantResourceType.Skill, Guid.NewGuid(), enabled: true);
        ok.ShouldBeFalse();
    }
}

/// <summary>
/// 测试专用 DbContext - 注册 grant junction 测试所需的 Agent + Provider + 三类 grant 实体。
/// Agent 配置含指向 Provider 的 FK，故必须一并注册其配置。
/// </summary>
internal sealed class AgentGrantDbContext : TnziDbContext<AgentGrantDbContext>
{
    public AgentGrantDbContext(
        DbContextOptions<AgentGrantDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AgentToolGrantConfiguration());
        modelBuilder.ApplyConfiguration(new AgentSkillGrantConfiguration());
        modelBuilder.ApplyConfiguration(new AgentKnowledgeGrantConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
