using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests;

/// <summary>
/// 端到端验证「junction grant 是工具组/技能/知识库的唯一权威来源」的接线：
/// 真实 AgentService（write + DTO 投影）+ 真实 AgentGrantService（reconcile/projection）
/// + 真实 AgentResolver（read），三者共享同一 SQLite 内存 DbContext。
/// 镜像 AgentGrantServiceTests 的自包含模式，因为 reconcile diff / 软删除 / DTO 覆盖 /
/// resolver 读取都必须经过真实的 EF Core 查询管道。
///
/// Agent 已删除 JSON 资源列（ToolGroups/SkillSlugs/KnowledgeBaseIds）—— grant 是唯一来源。
/// </summary>
public class AgentResourceGrantWiringTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AgentGrantWiringDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly AgentGrantService _grantService;
    private readonly AgentService _agentService;

    // resolver collaborators (mocked - we only care that grants reach the resolution)
    private readonly Mock<IAgentFactory> _agentFactory = new();
    private readonly Mock<IToolRegistry> _toolRegistry = new();
    private readonly Mock<IPromptTemplateEngine> _templateEngine = new();
    private readonly Mock<IAgentVersionRouter> _versionRouter = new();
    private readonly Mock<ILogger<AgentResolver>> _resolverLogger = new();
    private readonly AgentResolver _resolver;

    /// <summary>Captures the toolGroups argument the resolver passes to the factory.</summary>
    private IEnumerable<string>? _capturedToolGroups;

    /// <summary>Captures the toolNames (per-tool grants) argument the resolver passes to the factory.</summary>
    private IEnumerable<string>? _capturedToolNames;

    public AgentResourceGrantWiringTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);

        var options = new DbContextOptionsBuilder<AgentGrantWiringDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AgentGrantWiringDbContext(options, currentUserMock.Object);
        _context.Database.EnsureCreated();

        _serviceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();

        var agentRepo = new EFCoreRepository<AgentGrantWiringDbContext, Agent, Guid>(_context);
        var versionRepo = new EFCoreRepository<AgentGrantWiringDbContext, AgentVersion, Guid>(_context);

        _grantService = new AgentGrantService(
            _serviceProvider,
            new EFCoreRepository<AgentGrantWiringDbContext, AgentToolGrant, Guid>(_context),
            new EFCoreRepository<AgentGrantWiringDbContext, AgentSkillGrant, Guid>(_context),
            new EFCoreRepository<AgentGrantWiringDbContext, AgentKnowledgeGrant, Guid>(_context));

        _agentService = new AgentService(agentRepo, versionRepo, TestDispatchFacade.Wrap(Mock.Of<IAgentRuntime>()), _grantService, _serviceProvider);

        // resolver wiring - factory returns a stub executor and captures the toolGroups argument
        _versionRouter.Setup(v => v.RouteAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent a, CancellationToken _) => AgentVersionRouteResult.Passthrough(a));
        _templateEngine.Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
            .Returns(string.Empty);
        _agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, IEnumerable<string>?, Guid?, CancellationToken>(
                (_, _, _, _, toolGroups, _, _, _, _, toolNames, _, _) =>
                {
                    _capturedToolGroups = toolGroups?.ToList();
                    _capturedToolNames = toolNames?.ToList();
                })
            .ReturnsAsync(new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "stub" }));

        _resolver = new AgentResolver(
            _agentFactory.Object,
            new StaticOptionsMonitor<AIOptions>(new AIOptions()),
            agentRepo,
            _toolRegistry.Object,
            _templateEngine.Object,
            _versionRouter.Object,
            _grantService,
            _resolverLogger.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // =====================================================================
    // (a) An agent created with grants resolves them through AgentResolver
    // =====================================================================

    [Fact]
    public async Task CreateAsync_ThenResolve_ResolvesGrantsThroughResolver()
    {
        var kbId = Guid.NewGuid();
        var create = new CreateAgentDto
        {
            Name = "wired-agent",
            Provider = "OpenAI",
            Model = "gpt-4o",
            ToolGroups = ["fs", "shell"],
            SkillSlugs = ["writing"],
            KnowledgeBaseIds = [kbId]
        };

        var created = await _agentService.CreateAsync(create);
        created.Succeeded.ShouldBeTrue();
        var agentId = created.Data!.Id;
        _context.ChangeTracker.Clear();

        // The grant junctions were dual-written by CreateAsync.
        var grants = await _grantService.GetGrantsAsync(agentId);
        grants.ToolGroups.ShouldBe(new[] { "fs", "shell" }, ignoreOrder: true);
        grants.SkillSlugs.ShouldBe(new[] { "writing" });
        grants.KnowledgeBaseIds.ShouldBe(new[] { kbId });
        _context.ChangeTracker.Clear();

        // Resolver reads them: toolGroups reach the factory, skill/knowledge reach the resolution.
        var resolution = await _resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        _capturedToolGroups.ShouldBe(new[] { "fs", "shell" }, ignoreOrder: true);
        resolution.SkillSlugs.ShouldBe(new[] { "writing" });
        resolution.KnowledgeBaseIds.ShouldBe(new[] { kbId });
    }

    // =====================================================================
    // (b) AgentDto reflects the grants (authoritative on read)
    // =====================================================================

    [Fact]
    public async Task GetById_DtoReflectsGrants()
    {
        var kbId = Guid.NewGuid();
        var created = await _agentService.CreateAsync(new CreateAgentDto
        {
            Name = "dto-agent",
            Provider = "OpenAI",
            ToolGroups = ["fs"],
            SkillSlugs = ["research"],
            KnowledgeBaseIds = [kbId]
        });
        var agentId = created.Data!.Id;
        _context.ChangeTracker.Clear();

        var fetched = await _agentService.GetByIdAsync(agentId);

        fetched.Succeeded.ShouldBeTrue();
        fetched.Data!.ToolGroups.ShouldBe(new[] { "fs" });
        fetched.Data.SkillSlugs.ShouldBe(new[] { "research" });
        fetched.Data.KnowledgeBaseIds.ShouldBe(new[] { kbId });
    }

    [Fact]
    public async Task GetById_DtoReflectsGrantPriorityOrdering()
    {
        var created = await _agentService.CreateAsync(new CreateAgentDto
        {
            Name = "ordered-agent",
            Provider = "OpenAI",
            ToolGroups = ["fs", "git"]
        });
        var agentId = created.Data!.Id;
        _context.ChangeTracker.Clear();

        // bump git priority so it sorts ahead of fs in the projection
        var gitGrantId = await _context.Set<AgentToolGrant>()
            .Where(g => g.AgentId == agentId && g.ToolKey == "git")
            .Select(g => g.Id)
            .FirstAsync();
        _context.ChangeTracker.Clear();
        await _grantService.SetGrantPriorityAsync(GrantResourceType.Tool, gitGrantId, priority: 100);
        _context.ChangeTracker.Clear();

        var fetched = await _agentService.GetByIdAsync(agentId);

        fetched.Data!.ToolGroups.ShouldBe(new[] { "git", "fs" });
    }

    // =====================================================================
    // (c) Null-when-empty rule: no skill grants → SkillSlugs resolves as null
    //     (so SkillContextProvider falls back to name-wildcard filtering, not
    //     a "whitelist of nothing").
    // =====================================================================

    [Fact]
    public async Task Resolve_NoSkillGrants_SkillSlugsIsNull()
    {
        // Agent created WITHOUT any skill assignment → no skill grants, no JSON column.
        var created = await _agentService.CreateAsync(new CreateAgentDto
        {
            Name = "no-skills-agent",
            Provider = "OpenAI",
            ToolGroups = ["fs"]
        });
        var agentId = created.Data!.Id;
        _context.ChangeTracker.Clear();

        var resolution = await _resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        // null (not []) preserves the "no per-agent whitelist → name-wildcard fallback" behavior.
        resolution.SkillSlugs.ShouldBeNull();
        resolution.KnowledgeBaseIds.ShouldBeNull();
    }

    [Fact]
    public async Task Resolve_ClearedSkillGrants_SkillSlugsIsNull()
    {
        // Create with a skill, then clear it via UpdateAsync([]).
        var created = await _agentService.CreateAsync(new CreateAgentDto
        {
            Name = "cleared-skills-agent",
            Provider = "OpenAI",
            SkillSlugs = ["writing"]
        });
        var agentId = created.Data!.Id;
        _context.ChangeTracker.Clear();

        await _agentService.UpdateAsync(agentId, new UpdateAgentDto { SkillSlugs = [] });
        _context.ChangeTracker.Clear();

        // Both grants and the JSON column are now empty/empty → resolution null.
        var resolution = await _resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        resolution.SkillSlugs.ShouldBeNull();
    }

    // =====================================================================
    // (d) Reconcile through UpdateAsync changes resolution
    // =====================================================================

    [Fact]
    public async Task UpdateAsync_ReplacesGrants_ChangesResolution()
    {
        var created = await _agentService.CreateAsync(new CreateAgentDto
        {
            Name = "mutating-agent",
            Provider = "OpenAI",
            ToolGroups = ["fs"],
            SkillSlugs = ["writing"]
        });
        var agentId = created.Data!.Id;
        _context.ChangeTracker.Clear();

        // Replace tool groups + skills with a different set.
        var newKb = Guid.NewGuid();
        await _agentService.UpdateAsync(agentId, new UpdateAgentDto
        {
            ToolGroups = ["git", "web"],
            SkillSlugs = ["research"],
            KnowledgeBaseIds = [newKb]
        });
        _context.ChangeTracker.Clear();

        var resolution = await _resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        _capturedToolGroups.ShouldBe(new[] { "git", "web" }, ignoreOrder: true);
        resolution.SkillSlugs.ShouldBe(new[] { "research" });
        resolution.KnowledgeBaseIds.ShouldBe(new[] { newKb });
    }

    [Fact]
    public async Task UpdateAsync_NullResourceFields_LeavesGrantsUnchanged()
    {
        var created = await _agentService.CreateAsync(new CreateAgentDto
        {
            Name = "patch-agent",
            Provider = "OpenAI",
            ToolGroups = ["fs"],
            SkillSlugs = ["writing"]
        });
        var agentId = created.Data!.Id;
        _context.ChangeTracker.Clear();

        // PATCH only the name - resource fields null → grants untouched.
        await _agentService.UpdateAsync(agentId, new UpdateAgentDto { Name = "patched" });
        _context.ChangeTracker.Clear();

        var grants = await _grantService.GetGrantsAsync(agentId);
        grants.ToolGroups.ShouldBe(new[] { "fs" });
        grants.SkillSlugs.ShouldBe(new[] { "writing" });
    }

    // =====================================================================
    // No grants at all → resolver surfaces null/empty resources (no JSON
    // column fallback exists; grants are the sole source of truth).
    // =====================================================================

    [Fact]
    public async Task Resolve_AgentWithoutGrants_NoResourcesResolved()
    {
        // Seed the Agent ENTITY directly (no grants written through the grant service).
        var entity = new Agent
        {
            Name = "no-grants-agent",
            Provider = "OpenAI",
            Model = "gpt-4o",
            IsEnabled = true
        };
        _context.Set<Agent>().Add(entity);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var resolution = await _resolver.ResolveAgentAsync(entity.Id, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        // No tool groups reach the factory; skill/knowledge resolve as null (null-when-empty).
        (_capturedToolGroups is null || !_capturedToolGroups.Any()).ShouldBeTrue();
        resolution.SkillSlugs.ShouldBeNull();
        resolution.KnowledgeBaseIds.ShouldBeNull();
    }

    // =====================================================================
    // Clone carries the source's grants to the clone.
    // =====================================================================

    [Fact]
    public async Task CloneAsync_CarriesSourceGrantsToClone()
    {
        var kbId = Guid.NewGuid();
        var created = await _agentService.CreateAsync(new CreateAgentDto
        {
            Name = "clone-source",
            Provider = "OpenAI",
            ToolGroups = ["fs", "git"],
            SkillSlugs = ["writing"],
            KnowledgeBaseIds = [kbId]
        });
        var sourceId = created.Data!.Id;
        _context.ChangeTracker.Clear();

        var cloned = await _agentService.CloneAsync(sourceId, "clone-target");
        cloned.Succeeded.ShouldBeTrue();
        var cloneId = cloned.Data!.Id;
        cloneId.ShouldNotBe(sourceId);
        _context.ChangeTracker.Clear();

        var cloneGrants = await _grantService.GetGrantsAsync(cloneId);
        cloneGrants.ToolGroups.ShouldBe(new[] { "fs", "git" }, ignoreOrder: true);
        cloneGrants.SkillSlugs.ShouldBe(new[] { "writing" });
        cloneGrants.KnowledgeBaseIds.ShouldBe(new[] { kbId });

        // DTO of the clone reflects the carried grants.
        cloned.Data.ToolGroups!.ShouldBe(new[] { "fs", "git" }, ignoreOrder: true);
        cloned.Data.SkillSlugs!.ShouldBe(new[] { "writing" });
    }
}

/// <summary>
/// 测试专用 DbContext - Agent + Provider + AgentVersion + 三类 grant 实体配置。
/// AgentService 的版本快照需要 AgentVersion 配置；其余与 AgentGrantDbContext 一致。
/// </summary>
internal sealed class AgentGrantWiringDbContext : TnziDbContext<AgentGrantWiringDbContext>
{
    public AgentGrantWiringDbContext(
        DbContextOptions<AgentGrantWiringDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AgentVersionConfiguration());
        modelBuilder.ApplyConfiguration(new AgentToolGrantConfiguration());
        modelBuilder.ApplyConfiguration(new AgentSkillGrantConfiguration());
        modelBuilder.ApplyConfiguration(new AgentKnowledgeGrantConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
