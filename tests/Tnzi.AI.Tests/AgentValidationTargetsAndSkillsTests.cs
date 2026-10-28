using Microsoft.Data.Sqlite;
using Tnzi.AI.Tests.Integration;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests;

/// <summary>
/// Agent 校验的两项检查必须按运行时的真实形状判：多 Agent 模式的目标、技能注册表。
/// </summary>
/// <remarks>
/// 目标检查此前在配置根上找 <c>handoffTargets</c> 数组，而配置唯一的写法（<see cref="AgentExecutionConfigDto.Serialize"/>）
/// 是 <c>{"handoff":{"targets":{name:id}}}</c>，于是对任何 Agent 都「未配置目标」并通过，Router 模式根本不查；
/// 目标被删之后运行时只记一条 Warning、静默回源 Agent 的答复，这项检查是唯一看得见的信号。
/// 技能检查此前对每个 Agent 都要求技能注册表存在，不加载技能子模块的部署里所有 Agent 都被报成不健康。
/// 真实 SQLite + EFCoreRepository（多租户用例开着全局过滤器）。
/// </remarks>
public class AgentValidationTargetsAndSkillsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LoaderGrantDbContext _context;
    private readonly EFCoreRepository<LoaderGrantDbContext, Agent, Guid> _agentRepo;

    public AgentValidationTargetsAndSkillsTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(Guid.Empty);
        currentUser.Setup(m => m.IsAuthenticated).Returns(false);

        _context = new LoaderGrantDbContext(
            new DbContextOptionsBuilder<LoaderGrantDbContext>().UseSqlite(_connection).Options,
            currentUser.Object);
        _context.Database.EnsureCreated();
        _agentRepo = new EFCoreRepository<LoaderGrantDbContext, Agent, Guid>(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handoff_TargetDeleted_FailsTheTargetCheck()
    {
        var source = await SeedAgentAsync("source", AgentExecutionMode.Handoff,
            new AgentExecutionConfigDto { Handoff = new HandoffExecutionConfigDto { Targets = { ["billing"] = Guid.NewGuid() } } });

        var check = await TargetCheckAsync(source);

        check.Passed.ShouldBeFalse();
        check.Details.ShouldNotBeNull().ShouldContain("billing");
    }

    [Fact]
    public async Task Handoff_TargetDisabled_FailsTheTargetCheck()
    {
        var target = await SeedAgentAsync("billing", isEnabled: false);
        var source = await SeedAgentAsync("source", AgentExecutionMode.Handoff,
            new AgentExecutionConfigDto { Handoff = new HandoffExecutionConfigDto { Targets = { ["billing"] = target } } });

        var check = await TargetCheckAsync(source);

        check.Passed.ShouldBeFalse();
        check.Details.ShouldNotBeNull().ShouldContain("disabled");
    }

    [Fact]
    public async Task Handoff_AllTargetsPresent_PassesTheTargetCheck()
    {
        var billing = await SeedAgentAsync("billing");
        var support = await SeedAgentAsync("support");
        var source = await SeedAgentAsync("source", AgentExecutionMode.Handoff,
            new AgentExecutionConfigDto { Handoff = new HandoffExecutionConfigDto { Targets = { ["billing"] = billing, ["support"] = support } } });

        (await TargetCheckAsync(source)).Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task Router_TargetMissing_IsChecked()
    {
        var source = await SeedAgentAsync("router", AgentExecutionMode.Router,
            new AgentExecutionConfigDto { Router = new RouterExecutionConfigDto { Targets = { ["sales"] = Guid.NewGuid() } } });

        var check = await TargetCheckAsync(source);

        check.Passed.ShouldBeFalse();
        check.Details.ShouldNotBeNull().ShouldContain("sales");
    }

    [Fact]
    public async Task AgentAsTools_AllAgentsPresent_PassesTheTargetCheck()
    {
        var researcher = await SeedAgentAsync("researcher");
        var source = await SeedAgentAsync("orchestrator", AgentExecutionMode.AgentAsTools,
            new AgentExecutionConfigDto { AgentAsTools = new AgentAsToolsExecutionConfigDto { Agents = { ["researcher"] = researcher } } });

        (await TargetCheckAsync(source)).Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task Handoff_WithoutAnyTargets_FailsTheTargetCheck()
    {
        var source = await SeedAgentAsync("source", AgentExecutionMode.Handoff, configuration: null);

        (await TargetCheckAsync(source)).Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task SkillRegistryMissing_AgentWithoutSkillGrants_IsHealthy()
    {
        var agent = await SeedAgentAsync("plain");

        var result = await CreateService(skillRegistry: null).ValidateAsync(agent);

        result.Data!.Checks.Single(c => c.Name == "skill_registry").Passed.ShouldBeTrue();
        result.Data.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task SkillRegistryMissing_AgentWithSkillGrants_FailsTheSkillCheck()
    {
        var agent = await SeedAgentAsync("skilled");

        var result = await CreateService(skillRegistry: null, skillSlugs: ["pdf-forms"]).ValidateAsync(agent);

        result.Data!.Checks.Single(c => c.Name == "skill_registry").Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task SkillRegistryPresent_AgentWithSkillGrants_PassesTheSkillCheck()
    {
        var agent = await SeedAgentAsync("skilled");

        var result = await CreateService(skillRegistry: Mock.Of<ISkillRegistry>(), skillSlugs: ["pdf-forms"]).ValidateAsync(agent);

        result.Data!.Checks.Single(c => c.Name == "skill_registry").Passed.ShouldBeTrue();
    }

    /// <summary>租户调用者引用宿主共享的 YAML Agent 作为目标：运行时解析得到，校验也必须认。</summary>
    [Fact]
    public async Task TenantCaller_TargetIsASharedYamlAgent_PassesTheTargetCheck()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var mtEnabled = MsOptions.Create(new MultiTenancyOptions { Enabled = true });
        var user = new Mock<ICurrentUser>();
        user.Setup(m => m.Id).Returns(Guid.Empty);

        SharedAgentMtDbContext CreateContext(Guid? tenant) => new(
            new DbContextOptionsBuilder<SharedAgentMtDbContext>()
                .UseSqlite(connection)
                .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                    Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>()
                .Options,
            user.Object, new FixedTenant(tenant), mtEnabled);

        Guid sourceId;
        await using (var seed = CreateContext(tenant: null))
        {
            await seed.Database.EnsureCreatedAsync();
            var shared = new Agent { Name = "shared-billing", Provider = "OpenAI", Source = AgentSources.Yaml, IsEnabled = true };
            seed.Set<Agent>().Add(shared);
            await seed.SaveChangesAsync();

            var source = new Agent
            {
                Name = "tenant-source",
                Provider = "OpenAI",
                IsEnabled = true,
                TenantId = tenantId,
                ExecutionMode = AgentExecutionMode.Handoff,
                Configuration = AgentExecutionConfigDto.Serialize(new AgentExecutionConfigDto
                {
                    Handoff = new HandoffExecutionConfigDto { Targets = { ["billing"] = shared.Id } }
                })
            };
            seed.Set<Agent>().Add(source);
            await seed.SaveChangesAsync();
            sourceId = source.Id;
        }

        await using var tenantContext = CreateContext(tenantId);
        var services = new ServiceCollection().AddLogging()
            .AddSingleton(mtEnabled)
            .AddSingleton<ICurrentTenant>(new FixedTenant(tenantId))
            .BuildServiceProvider();
        var service = new AgentValidationService(
            new EFCoreRepository<SharedAgentMtDbContext, Agent, Guid>(tenantContext),
            ChatClientFactory(), ToolRegistry(), GrantService([]), services, skillRegistry: null);

        var result = await service.ValidateAsync(sourceId);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Checks.Single(c => c.Name == "handoff_targets").Passed.ShouldBeTrue(result.Data.Checks.Single(c => c.Name == "handoff_targets").Details);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<ValidationCheckDto> TargetCheckAsync(Guid agentId)
    {
        var result = await CreateService(Mock.Of<ISkillRegistry>()).ValidateAsync(agentId);
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Checks.Single(c => c.Name == "handoff_targets");
    }

    private AgentValidationService CreateService(ISkillRegistry? skillRegistry, IReadOnlyList<string>? skillSlugs = null)
        => new(
            _agentRepo,
            ChatClientFactory(),
            ToolRegistry(),
            GrantService(skillSlugs ?? []),
            new ServiceCollection().AddLogging().BuildServiceProvider(),
            skillRegistry);

    private static IChatClientFactory ChatClientFactory()
    {
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.IsProviderAvailable(It.IsAny<string>())).Returns(true);
        factory.Setup(f => f.GetDefaultModel(It.IsAny<string?>())).Returns("gpt-4o");
        factory.Setup(f => f.GetAvailableProviders()).Returns(["OpenAI"]);
        return factory.Object;
    }

    private static IToolRegistry ToolRegistry()
    {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetAllGroupNames()).Returns([]);
        return registry.Object;
    }

    private static IAgentGrantService GrantService(IReadOnlyList<string> skillSlugs)
    {
        var grants = new Mock<IAgentGrantService>();
        grants.Setup(g => g.GetGrantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentGrantsProjection { SkillSlugs = skillSlugs });
        return grants.Object;
    }

    private async Task<Guid> SeedAgentAsync(
        string name, AgentExecutionMode mode = AgentExecutionMode.Single, AgentExecutionConfigDto? configuration = null, bool isEnabled = true)
    {
        var agent = new Agent
        {
            Name = name,
            Provider = "OpenAI",
            Model = "gpt-4o",
            IsEnabled = isEnabled,
            ExecutionMode = mode,
            Configuration = AgentExecutionConfigDto.Serialize(configuration)
        };
        _context.Set<Agent>().Add(agent);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return agent.Id;
    }

    private sealed class FixedTenant(Guid? id) : ICurrentTenant
    {
        public Guid? Id { get; } = id;
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
