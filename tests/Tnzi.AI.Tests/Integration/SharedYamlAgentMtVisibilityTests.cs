using Microsoft.Data.Sqlite;
using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// 多租户开启时，YAML 同步而来的 Agent（<c>Source=yaml</c>、<c>TenantId=null</c>）是宿主级共享定义：
/// 每个租户都能解析、列出、克隆、为它建会话，但不能修改或删除；租户自己的 Agent 仍然按租户隔离。
/// </summary>
/// <remarks>
/// 真实的全局过滤器（多租户开启的模型 + 严格等值的租户过滤器）+ 真实的 AgentService / AgentGrantService /
/// AgentResolver / AgentVersionRouter / AgentThreadService。只有 LLM 工厂与模板引擎是桩。
/// </remarks>
public class SharedYamlAgentMtVisibilityTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private readonly SqliteConnection _connection;
    private readonly Guid _yamlAgentId;
    private readonly Guid _hostDatabaseAgentId;
    private readonly Guid _tenantAAgentId;

    private IEnumerable<string>? _capturedToolGroups;

    public SharedYamlAgentMtVisibilityTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        // 种子上下文没有租户：行上的 TenantId 按显式赋值落库（审计戳只在为空时补）。
        using var seed = CreateContext(tenantId: null);
        seed.Database.EnsureCreated();

        var yaml = new Agent { Name = "shared-yaml", Provider = "OpenAI", Source = AgentSources.Yaml };
        var hostDb = new Agent { Name = "host-database", Provider = "OpenAI", Source = AgentSources.Database };
        var tenantA = new Agent { Name = "tenant-a-own", Provider = "OpenAI", TenantId = TenantA };
        seed.Set<Agent>().AddRange(yaml, hostDb, tenantA);
        seed.SaveChangesAsync().GetAwaiter().GetResult();

        seed.Set<AgentToolGrant>().Add(new AgentToolGrant { AgentId = yaml.Id, GrantType = GrantType.Group, ToolKey = "web" });
        seed.Set<AgentToolGrant>().Add(new AgentToolGrant { AgentId = tenantA.Id, GrantType = GrantType.Group, ToolKey = "fs", TenantId = TenantA });
        seed.Set<AgentVersion>().Add(new AgentVersion
        {
            AgentId = yaml.Id,
            Version = 1,
            ConfigSnapshot = JsonSerializer.Serialize(new { Name = "shared-yaml", Provider = "OpenAI", ToolGroups = new[] { "web" } })
        });
        seed.SaveChangesAsync().GetAwaiter().GetResult();

        _yamlAgentId = yaml.Id;
        _hostDatabaseAgentId = hostDb.Id;
        _tenantAAgentId = tenantA.Id;
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>前提：模型确实开着多租户，普通查询确实看不见共享行 —— 否则下面的用例什么都证明不了。</summary>
    [Fact]
    public async Task Precondition_PlainTenantQuery_CannotSeeTheSharedRow()
    {
        await using var ctx = CreateContext(TenantA);
        ctx.IsMultiTenancyEnabled.ShouldBeTrue();
        (await ctx.Set<Agent>().AnyAsync(a => a.Id == _yamlAgentId)).ShouldBeFalse();
    }

    [Fact]
    public async Task TenantResolver_ResolvesSharedYamlAgent_WithItsHostGrants()
    {
        await using var ctx = CreateContext(TenantA);
        var resolver = CreateResolver(ctx, TenantA);

        var resolution = await resolver.ResolveAgentAsync(_yamlAgentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue(resolution.ErrorCode);
        _capturedToolGroups.ShouldNotBeNull("the shared agent's grants are host rows and must resolve too");
        _capturedToolGroups!.ShouldBe(["web"]);
    }

    [Fact]
    public async Task TenantList_ContainsSharedYamlAgent_AndOwnAgent_ButNotHostDatabaseOrOtherTenantAgents()
    {
        await using var ctxB = CreateContext(TenantB);
        var listB = await CreateAgentService(ctxB, TenantB).GetListAsync(new AgentListQueryDto { PageIndex = 1, PageSize = 50 });

        listB.Succeeded.ShouldBeTrue(listB.Message);
        var idsB = listB.Data!.Items.Select(a => a.Id).ToList();
        idsB.ShouldContain(_yamlAgentId);
        idsB.ShouldNotContain(_tenantAAgentId, "tenant A's own agent stays tenant-isolated");
        idsB.ShouldNotContain(_hostDatabaseAgentId, "only YAML definitions are shared, not every host row");
        listB.Data.Items.Single(a => a.Id == _yamlAgentId).ToolGroups.ShouldBe(["web"]);

        await using var ctxA = CreateContext(TenantA);
        var listA = await CreateAgentService(ctxA, TenantA).GetListAsync(new AgentListQueryDto { PageIndex = 1, PageSize = 50 });
        listA.Data!.Items.Select(a => a.Id).ShouldBe([_yamlAgentId, _tenantAAgentId], ignoreOrder: true);
    }

    /// <summary>对照：共享规则不能把租户自己的 Agent 放出去。</summary>
    [Fact]
    public async Task OtherTenant_CannotResolveOrReadATenantsOwnAgent()
    {
        await using var ctx = CreateContext(TenantB);

        var resolution = await CreateResolver(ctx, TenantB).ResolveAgentAsync(_tenantAAgentId, null, null, null, CancellationToken.None);
        resolution.IsSuccess.ShouldBeFalse();
        resolution.ErrorCode.ShouldBe(ErrorCodes.AgentNotFound);

        var get = await CreateAgentService(ctx, TenantB).GetByIdAsync(_tenantAAgentId);
        get.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Tenant_CannotUpdateOrDeleteSharedYamlAgent()
    {
        await using var ctx = CreateContext(TenantA);
        var service = CreateAgentService(ctx, TenantA);

        (await service.GetByIdAsync(_yamlAgentId)).Succeeded.ShouldBeTrue("read access is granted");

        var update = await service.UpdateAsync(_yamlAgentId, new UpdateAgentDto { Name = "hijacked" });
        update.Code.ShouldBe(403);
        update.ErrorCode.ShouldBe(ErrorCodes.AgentSharedDefinitionReadOnly);

        var delete = await service.DeleteAsync(_yamlAgentId);
        delete.Code.ShouldBe(403);

        var abTest = await service.StopAbTestAsync(_yamlAgentId);
        abTest.Code.ShouldBe(403);

        await using var host = CreateContext(tenantId: null);
        var stored = await host.Set<Agent>().AsNoTracking().SingleAsync(a => a.Id == _yamlAgentId);
        stored.Name.ShouldBe("shared-yaml");
        stored.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Tenant_CanCloneSharedYamlAgent_IntoAnOwnedAgentWithItsGrants()
    {
        await using var ctx = CreateContext(TenantA);
        var clone = await CreateAgentService(ctx, TenantA).CloneAsync(_yamlAgentId, "my-copy");

        clone.Succeeded.ShouldBeTrue(clone.Message);
        clone.Data!.ToolGroups.ShouldBe(["web"]);

        await using var host = CreateContext(tenantId: null);
        var stored = await host.Set<Agent>().IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == clone.Data.Id);
        stored.TenantId.ShouldBe(TenantA);
        stored.Source.ShouldBe(AgentSources.Database);
    }

    [Fact]
    public async Task Tenant_CanOpenAThreadOnSharedYamlAgent()
    {
        await using var ctx = CreateContext(TenantA);
        var threads = new AgentThreadService(
            new EFCoreRepository<SharedAgentMtDbContext, AgentThreadEntity, Guid>(ctx),
            new EFCoreRepository<SharedAgentMtDbContext, AgentThreadMessage, Guid>(ctx),
            new EFCoreRepository<SharedAgentMtDbContext, Agent, Guid>(ctx),
            ServiceProvider(),
            new StubCurrentTenant(TenantA),
            MtEnabled());

        var result = await threads.CreateAsync(new CreateAgentThreadDto { AgentId = _yamlAgentId });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.AgentName.ShouldBe("shared-yaml");
    }

    [Fact]
    public async Task TenantVersionRouter_SeesTheHostVersionsOfASharedAgent()
    {
        await using var ctx = CreateContext(TenantA);
        var shared = await CreateAgentServiceRepo(ctx).AsQueryable().IgnoreQueryFilters().SingleAsync(a => a.Id == _yamlAgentId);
        var router = new AgentVersionRouter(
            new EFCoreRepository<SharedAgentMtDbContext, AgentVersion, Guid>(ctx),
            NullLogger<AgentVersionRouter>.Instance,
            currentUser: null,
            currentTenant: new StubCurrentTenant(TenantA),
            multiTenancyOptions: MtEnabled());

        var routed = await router.RouteToVersionAsync(shared, 1, CancellationToken.None);

        routed.SelectedVersion.ShouldBe(1, "a pinned version of a shared agent must not silently fall back to passthrough");
    }

    /// <summary>多 Agent 编排里的子 / 目标 Agent 与主路径同一条可见性。</summary>
    [Fact]
    public async Task TenantStrategyLoader_ResolvesSharedYamlAgentAsChild()
    {
        await using var ctx = CreateContext(TenantA);
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(r => r.GetToolsByGroups(It.IsAny<IEnumerable<string>>())).Returns([]);
        toolRegistry.Setup(r => r.GetToolsByNames(It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>?>())).Returns([]);

        var services = new ServiceCollection();
        services.AddSingleton(MtEnabled());
        services.AddSingleton<ICurrentTenant>(new StubCurrentTenant(TenantA));
        services.AddSingleton<IAgentGrantService>(CreateGrantService(ctx, TenantA));
        services.AddSingleton<IUserToolPermissionResolver>(
            new UserToolPermissionResolver(toolRegistry.Object, Mock.Of<ILogger<UserToolPermissionResolver>>()));

        var factory = new Mock<IAgentFactory>();
        factory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "stub" }));

        var context = new ExecutionStrategyContext
        {
            AgentFactory = factory.Object,
            AgentRepository = new EFCoreRepository<SharedAgentMtDbContext, Agent, Guid>(ctx),
            ServiceProvider = services.BuildServiceProvider(),
            Logger = NullLogger.Instance
        };

        (await ExecutionStrategyAgentLoader.ResolveAgentAsync(_yamlAgentId, context, CancellationToken.None)).ShouldNotBeNull();
        (await ExecutionStrategyAgentLoader.ResolveAgentAsync(_hostDatabaseAgentId, context, CancellationToken.None))
            .ShouldBeNull("only YAML definitions are shared");
    }

    // ── factories ────────────────────────────────────────────────────────────

    private SharedAgentMtDbContext CreateContext(Guid? tenantId)
    {
        var user = new Mock<ICurrentUser>();
        user.Setup(m => m.Id).Returns(Guid.Empty);
        user.Setup(m => m.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<SharedAgentMtDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>()
            .Options;

        return new SharedAgentMtDbContext(options, user.Object, new StubCurrentTenant(tenantId), MtEnabled());
    }

    private static IOptions<MultiTenancyOptions> MtEnabled() => MsOptions.Create(new MultiTenancyOptions { Enabled = true });

    private static IServiceProvider ServiceProvider() => new ServiceCollection().AddLogging().BuildServiceProvider();

    private static EFCoreRepository<SharedAgentMtDbContext, Agent, Guid> CreateAgentServiceRepo(SharedAgentMtDbContext ctx) => new(ctx);

    private static AgentGrantService CreateGrantService(SharedAgentMtDbContext ctx, Guid tenantId)
        => new(
            ServiceProvider(),
            new EFCoreRepository<SharedAgentMtDbContext, AgentToolGrant, Guid>(ctx),
            new EFCoreRepository<SharedAgentMtDbContext, AgentSkillGrant, Guid>(ctx),
            new EFCoreRepository<SharedAgentMtDbContext, AgentKnowledgeGrant, Guid>(ctx),
            new StubCurrentTenant(tenantId),
            MtEnabled());

    private static AgentService CreateAgentService(SharedAgentMtDbContext ctx, Guid tenantId)
        => new(
            new EFCoreRepository<SharedAgentMtDbContext, Agent, Guid>(ctx),
            new EFCoreRepository<SharedAgentMtDbContext, AgentVersion, Guid>(ctx),
            TestDispatchFacade.Wrap(Mock.Of<IAgentRuntime>()),
            CreateGrantService(ctx, tenantId),
            ServiceProvider(),
            new StubCurrentTenant(tenantId),
            MtEnabled());

    private AgentResolver CreateResolver(SharedAgentMtDbContext ctx, Guid tenantId)
    {
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(r => r.GetToolsByGroups(It.IsAny<IEnumerable<string>>())).Returns([]);
        toolRegistry.Setup(r => r.GetToolsByNames(It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>?>())).Returns([]);

        var templateEngine = new Mock<IPromptTemplateEngine>();
        templateEngine.Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>())).Returns(string.Empty);

        var factory = new Mock<IAgentFactory>();
        factory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, IEnumerable<string>?, Guid?, CancellationToken>(
                (_, _, _, _, toolGroups, _, _, _, _, _, _, _) => _capturedToolGroups = toolGroups?.ToList())
            .ReturnsAsync(new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "stub" }));

        var tenant = new StubCurrentTenant(tenantId);
        return new AgentResolver(
            factory.Object,
            new StaticOptionsMonitor<AIOptions>(new AIOptions()),
            new EFCoreRepository<SharedAgentMtDbContext, Agent, Guid>(ctx),
            new UserToolPermissionResolver(toolRegistry.Object, Mock.Of<ILogger<UserToolPermissionResolver>>()),
            templateEngine.Object,
            new AgentVersionRouter(
                new EFCoreRepository<SharedAgentMtDbContext, AgentVersion, Guid>(ctx),
                NullLogger<AgentVersionRouter>.Instance,
                currentUser: null,
                currentTenant: tenant,
                multiTenancyOptions: MtEnabled()),
            CreateGrantService(ctx, tenantId),
            Mock.Of<ILogger<AgentResolver>>(),
            currentTenant: tenant,
            multiTenancyOptions: MtEnabled());
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

/// <summary>多租户开启的最小 AI 模型：Agent、它的授权与版本、会话线程。</summary>
internal sealed class SharedAgentMtDbContext : TnziDbContext<SharedAgentMtDbContext>
{
    public SharedAgentMtDbContext(
        DbContextOptions<SharedAgentMtDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant,
        IOptions<MultiTenancyOptions> multiTenancyOptions)
        : base(options, currentUser, currentTenant, multiTenancyOptions: multiTenancyOptions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new AgentConfiguration().RegisterTo(modelBuilder, this);
        new ProviderConfiguration().RegisterTo(modelBuilder, this);
        new AgentVersionConfiguration().RegisterTo(modelBuilder, this);
        new AgentToolGrantConfiguration().RegisterTo(modelBuilder, this);
        new AgentSkillGrantConfiguration().RegisterTo(modelBuilder, this);
        new AgentKnowledgeGrantConfiguration().RegisterTo(modelBuilder, this);
        new AgentThreadConfiguration().RegisterTo(modelBuilder, this);
        new AgentThreadMessageConfiguration().RegisterTo(modelBuilder, this);

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
