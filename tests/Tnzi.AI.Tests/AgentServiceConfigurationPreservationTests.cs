using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests;

/// <summary>
/// <c>Agent.Configuration</c> 同时装着三件互不相干的东西：执行配置（handoff / router / agentAsTools）、
/// 进行中的 A/B 实验（abTest）与按 Agent 的开关（如 disableContextProviders）。
/// 改执行配置、回滚版本都只能动属于自己的那一部分。
/// 另外覆盖版本快照写失败时不拖垮这次更新（快照是尽力而为）。
/// </summary>
public class AgentServiceConfigurationPreservationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AgentGrantWiringDbContext> _options;
    private readonly ICurrentUser _currentUser;
    private readonly AgentGrantWiringDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly AgentGrantService _grantService;

    public AgentServiceConfigurationPreservationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);
        _currentUser = currentUserMock.Object;

        _options = new DbContextOptionsBuilder<AgentGrantWiringDbContext>().UseSqlite(_connection).Options;
        _context = new AgentGrantWiringDbContext(_options, _currentUser);
        _context.Database.EnsureCreated();

        _serviceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();
        _grantService = new AgentGrantService(
            _serviceProvider,
            new EFCoreRepository<AgentGrantWiringDbContext, AgentToolGrant, Guid>(_context),
            new EFCoreRepository<AgentGrantWiringDbContext, AgentSkillGrant, Guid>(_context),
            new EFCoreRepository<AgentGrantWiringDbContext, AgentKnowledgeGrant, Guid>(_context));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AgentService CreateService(IRepository<AgentVersion, Guid>? versionRepository = null)
        => new(
            new EFCoreRepository<AgentGrantWiringDbContext, Agent, Guid>(_context),
            versionRepository ?? new EFCoreRepository<AgentGrantWiringDbContext, AgentVersion, Guid>(_context),
            TestDispatchFacade.Wrap(Mock.Of<IAgentRuntime>()),
            _grantService,
            _serviceProvider);

    /// <summary>建一个 Agent，再改两次名字，得到版本 1、2 供 A/B 实验引用。</summary>
    private async Task<Guid> CreateAgentWithTwoVersionsAsync(AgentService service)
    {
        var created = await service.CreateAsync(new CreateAgentDto { Name = "ab-agent", Provider = "OpenAI" });
        created.Succeeded.ShouldBeTrue(created.Message);
        var agentId = created.Data!.Id;
        (await service.UpdateAsync(agentId, new UpdateAgentDto { Description = "first edit" })).Succeeded.ShouldBeTrue();
        (await service.UpdateAsync(agentId, new UpdateAgentDto { Description = "second edit" })).Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();
        return agentId;
    }

    private async Task<string?> ReadConfigurationAsync(Guid agentId)
    {
        _context.ChangeTracker.Clear();
        return await _context.Set<Agent>().AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => a.Configuration)
            .FirstAsync();
    }

    [Fact]
    public async Task UpdateAsync_ExecutionModeChange_KeepsTheRunningAbTest()
    {
        var service = CreateService();
        var agentId = await CreateAgentWithTwoVersionsAsync(service);
        (await service.ConfigureAbTestAsync(agentId, new ConfigureAbTestDto { VersionA = 1, VersionB = 2, TrafficPercentB = 10 }))
            .Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();

        var targetId = Guid.NewGuid();
        var updated = await service.UpdateAsync(agentId, new UpdateAgentDto
        {
            ExecutionMode = AgentExecutionMode.Handoff,
            ExecutionConfig = new AgentExecutionConfigDto
            {
                Handoff = new HandoffExecutionConfigDto { Targets = new Dictionary<string, Guid> { ["billing"] = targetId } }
            }
        });

        updated.Succeeded.ShouldBeTrue(updated.Message);
        var configuration = await ReadConfigurationAsync(agentId);
        var abTest = AgentVersionRouter.ExtractAbTestConfig(configuration);
        abTest.ShouldNotBeNull("changing the execution config must not stop the experiment");
        abTest.Enabled.ShouldBeTrue();
        abTest.TrafficPercentB.ShouldBe(10);
        AgentExecutionConfigDto.Deserialize(configuration)!.Handoff!.Targets["billing"].ShouldBe(targetId);
    }

    [Fact]
    public async Task UpdateAsync_ExecutionModeChangeWithoutConfig_DropsOnlyTheExecutionConfig()
    {
        var service = CreateService();
        var created = await service.CreateAsync(new CreateAgentDto { Name = "switches", Provider = "OpenAI" });
        var agentId = created.Data!.Id;
        var agent = await _context.Set<Agent>().FirstAsync(a => a.Id == agentId);
        agent.ExecutionMode = AgentExecutionMode.Router;
        agent.Configuration = """{"router":{"targets":{"specialist":"11111111-1111-1111-1111-111111111111"}},"disableContextProviders":true}""";
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var updated = await service.UpdateAsync(agentId, new UpdateAgentDto { ExecutionMode = AgentExecutionMode.Single });

        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.ExecutionConfig.ShouldBeNull();
        var configuration = await ReadConfigurationAsync(agentId);
        configuration.ShouldNotBeNull();
        configuration.ShouldNotContain("router");
        configuration.ShouldContain("disableContextProviders");
    }

    [Fact]
    public async Task RollbackToVersionAsync_KeepsTheCurrentAbTest()
    {
        var service = CreateService();
        var agentId = await CreateAgentWithTwoVersionsAsync(service);
        (await service.ConfigureAbTestAsync(agentId, new ConfigureAbTestDto { VersionA = 1, VersionB = 2, TrafficPercentB = 30 }))
            .Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();

        // 版本 1 是实验开始之前的快照，里面没有 abTest
        var rolledBack = await service.RollbackToVersionAsync(agentId, 1);

        rolledBack.Succeeded.ShouldBeTrue(rolledBack.Message);
        var abTest = AgentVersionRouter.ExtractAbTestConfig(await ReadConfigurationAsync(agentId));
        abTest.ShouldNotBeNull("a rollback restores the agent's definition, it does not stop the running experiment");
        abTest.TrafficPercentB.ShouldBe(30);
    }

    [Fact]
    public async Task RollbackToVersionAsync_DoesNotRestartAnExperimentRecordedInTheSnapshot()
    {
        var service = CreateService();
        var agentId = await CreateAgentWithTwoVersionsAsync(service);
        (await service.ConfigureAbTestAsync(agentId, new ConfigureAbTestDto { VersionA = 1, VersionB = 2, TrafficPercentB = 50 }))
            .Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();
        // 版本 3 的快照里记着这个实验
        (await service.UpdateAsync(agentId, new UpdateAgentDto { Description = "third edit" })).Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();
        (await service.StopAbTestAsync(agentId)).Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();

        var rolledBack = await service.RollbackToVersionAsync(agentId, 3);

        rolledBack.Succeeded.ShouldBeTrue(rolledBack.Message);
        AgentVersionRouter.ExtractAbTestConfig(await ReadConfigurationAsync(agentId))
            .ShouldBeNull("a stopped experiment must not come back because an old snapshot still carries it");
    }

    [Fact]
    public async Task CloneAsync_DoesNotCarryTheSourceAbTest()
    {
        // A/B 实验引用的是源 Agent 的版本号，克隆体没有任何版本：带过去只会每次请求都找不到版本
        var service = CreateService();
        var agentId = await CreateAgentWithTwoVersionsAsync(service);
        var source = await _context.Set<Agent>().FirstAsync(a => a.Id == agentId);
        source.Configuration = """{"disableContextProviders":true}""";
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        (await service.ConfigureAbTestAsync(agentId, new ConfigureAbTestDto { VersionA = 1, VersionB = 2, TrafficPercentB = 20 }))
            .Succeeded.ShouldBeTrue();
        _context.ChangeTracker.Clear();

        var cloned = await service.CloneAsync(agentId, "ab-clone");

        cloned.Succeeded.ShouldBeTrue(cloned.Message);
        var configuration = await ReadConfigurationAsync(cloned.Data!.Id);
        AgentVersionRouter.ExtractAbTestConfig(configuration).ShouldBeNull();
        configuration.ShouldNotBeNull();
        configuration.ShouldContain("disableContextProviders");
    }

    [Fact]
    public async Task UpdateAsync_VersionSnapshotLosesARace_StillSavesTheEdit()
    {
        // 两个管理员几乎同时保存：另一个请求先占用了同一个版本号，本次快照插入撞上 (AgentId, Version) 唯一索引
        var racingVersions = new RacingVersionRepository(_context, () => new AgentGrantWiringDbContext(_options, _currentUser));
        var service = CreateService(racingVersions);
        var created = await service.CreateAsync(new CreateAgentDto { Name = "contested", Provider = "OpenAI" });
        var agentId = created.Data!.Id;
        _context.ChangeTracker.Clear();

        racingVersions.RaceNextInsert = true;
        var updated = await service.UpdateAsync(agentId, new UpdateAgentDto { Description = "saved by the second admin" });

        updated.Succeeded.ShouldBeTrue(updated.Message);
        racingVersions.Raced.ShouldBeTrue("the test must actually have provoked the unique-index collision");
        _context.ChangeTracker.Clear();
        (await _context.Set<Agent>().AsNoTracking().FirstAsync(a => a.Id == agentId))
            .Description.ShouldBe("saved by the second admin");
        (await _context.Set<AgentVersion>().AsNoTracking().CountAsync(v => v.AgentId == agentId)).ShouldBe(1);
    }

    /// <summary>
    /// 在本次插入之前，用另一条连接上下文先写入同一个 (AgentId, Version)，模拟并发保存的赢家。
    /// </summary>
    private sealed class RacingVersionRepository : EFCoreRepository<AgentGrantWiringDbContext, AgentVersion, Guid>
    {
        private readonly Func<AgentGrantWiringDbContext> _otherContextFactory;

        public RacingVersionRepository(AgentGrantWiringDbContext context, Func<AgentGrantWiringDbContext> otherContextFactory)
            : base(context)
        {
            _otherContextFactory = otherContextFactory;
        }

        public bool RaceNextInsert { get; set; }
        public bool Raced { get; private set; }

        public override async Task InsertAsync(AgentVersion entity, CancellationToken cancellationToken = default)
        {
            if (RaceNextInsert)
            {
                RaceNextInsert = false;
                await using var other = _otherContextFactory();
                other.Set<AgentVersion>().Add(new AgentVersion
                {
                    AgentId = entity.AgentId,
                    Version = entity.Version,
                    ConfigSnapshot = entity.ConfigSnapshot
                });
                await other.SaveChangesAsync(cancellationToken);
                Raced = true;
            }

            await base.InsertAsync(entity, cancellationToken);
        }
    }
}
