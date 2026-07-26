using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests;

/// <summary>
/// Verifies that <see cref="ExecutionStrategyAgentLoader.ResolveAgentAsync"/> - the CHILD/target-agent loader
/// used by Handoff / Router / AgentAsTools strategies - projects per-tool grants (GrantType=Tool) and passes
/// them to the factory as <c>toolNames</c>. Without this a child agent in a multi-agent flow silently loses
/// its individually granted tools (only tool groups would survive).
///
/// Mirrors <see cref="AgentResourceGrantWiringTests"/>: a real <see cref="AgentGrantService"/> over a SQLite
/// in-memory DbContext (so the Group/Tool split in the projection runs through real EF Core) + a real Agent
/// repository, with a mock <see cref="IAgentFactory"/> capturing the toolGroups / toolNames the loader passes.
///
/// SkillSlugs / KnowledgeBaseIds are intentionally NOT wired on this path (they are consumed only by
/// ContextInjectionMiddleware, which child agents executed via a bare AgentExecutor.ExecuteAsync never run
/// through). See the NOTE in ExecutionStrategyAgentLoader for the rationale.
/// </summary>
public class ExecutionStrategyAgentLoaderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LoaderGrantDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly AgentGrantService _grantService;
    private readonly EFCoreRepository<LoaderGrantDbContext, Agent, Guid> _agentRepo;
    private readonly Mock<IAgentFactory> _agentFactory = new();

    /// <summary>Captures the toolGroups argument the loader passes to the factory.</summary>
    private IEnumerable<string>? _capturedToolGroups;

    /// <summary>Captures the toolNames (per-tool grants) argument the loader passes to the factory.</summary>
    private IEnumerable<string>? _capturedToolNames;

    public ExecutionStrategyAgentLoaderTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);

        var options = new DbContextOptionsBuilder<LoaderGrantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new LoaderGrantDbContext(options, currentUserMock.Object);
        _context.Database.EnsureCreated();

        // Real grant service so GetGrantsAsync's Group/Tool split runs through EF Core.
        var grantSp = new ServiceCollection().AddLogging().BuildServiceProvider();
        _grantService = new AgentGrantService(
            grantSp,
            new EFCoreRepository<LoaderGrantDbContext, AgentToolGrant, Guid>(_context),
            new EFCoreRepository<LoaderGrantDbContext, AgentSkillGrant, Guid>(_context),
            new EFCoreRepository<LoaderGrantDbContext, AgentKnowledgeGrant, Guid>(_context));

        _agentRepo = new EFCoreRepository<LoaderGrantDbContext, Agent, Guid>(_context);

        // The context.ServiceProvider must resolve IAgentGrantService (the loader fetches it via GetRequiredService).
        _serviceProvider = new ServiceCollection()
            .AddSingleton<IAgentGrantService>(_grantService)
            .BuildServiceProvider();

        // Factory stub captures the toolGroups / toolNames arguments and returns a throwaway executor.
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
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private ExecutionStrategyContext BuildContext() => new()
    {
        AgentFactory = _agentFactory.Object,
        AgentRepository = _agentRepo,
        ServiceProvider = _serviceProvider,
        Logger = NullLogger.Instance
    };

    private async Task<Guid> SeedAgentAsync(IEnumerable<(GrantType type, string toolKey)>? toolGrants = null)
    {
        var agent = new Agent
        {
            Name = "child-agent",
            Provider = "OpenAI",
            Model = "gpt-4o",
            IsEnabled = true
        };
        _context.Set<Agent>().Add(agent);

        if (toolGrants != null)
        {
            foreach (var (type, toolKey) in toolGrants)
            {
                _context.Set<AgentToolGrant>().Add(new AgentToolGrant
                {
                    AgentId = agent.Id,
                    GrantType = type,
                    ToolKey = toolKey,
                    IsEnabled = true,
                    Priority = 0
                });
            }
        }

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return agent.Id;
    }

    // =====================================================================
    // The clear, safe win: a child agent with a GrantType=Tool grant gets
    // that tool passed to the factory as toolNames.
    // =====================================================================

    [Fact]
    public async Task ResolveAgent_WithPerToolGrant_PassesToolNamesToFactory()
    {
        var agentId = await SeedAgentAsync(
        [
            (GrantType.Group, "fs"),
            (GrantType.Tool, "read_file")
        ]);

        var executor = await ExecutionStrategyAgentLoader.ResolveAgentAsync(agentId, BuildContext(), CancellationToken.None);

        executor.ShouldNotBeNull();
        // Group grants still flow as toolGroups (regression guard).
        _capturedToolGroups.ShouldBe(new[] { "fs" });
        // Per-tool grant flows as toolNames - the gap this fix closes.
        _capturedToolNames.ShouldBe(new[] { "read_file" });
    }

    [Fact]
    public async Task ResolveAgent_WithMultiplePerToolGrants_PassesAllToolNames()
    {
        var agentId = await SeedAgentAsync(
        [
            (GrantType.Tool, "read_file"),
            (GrantType.Tool, "write_file")
        ]);

        await ExecutionStrategyAgentLoader.ResolveAgentAsync(agentId, BuildContext(), CancellationToken.None);

        _capturedToolNames.ShouldBe(new[] { "read_file", "write_file" }, ignoreOrder: true);
    }

    // =====================================================================
    // null-when-empty: no per-tool grants → toolNames is null (not []),
    // matching the primary path's null-when-empty semantics.
    // =====================================================================

    [Fact]
    public async Task ResolveAgent_WithOnlyGroupGrants_ToolNamesIsNull()
    {
        var agentId = await SeedAgentAsync([(GrantType.Group, "fs")]);

        await ExecutionStrategyAgentLoader.ResolveAgentAsync(agentId, BuildContext(), CancellationToken.None);

        _capturedToolGroups.ShouldBe(new[] { "fs" });
        _capturedToolNames.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveAgent_WithNoGrants_BothToolGroupsAndToolNamesAreNull()
    {
        var agentId = await SeedAgentAsync();

        var executor = await ExecutionStrategyAgentLoader.ResolveAgentAsync(agentId, BuildContext(), CancellationToken.None);

        executor.ShouldNotBeNull();
        _capturedToolGroups.ShouldBeNull();
        _capturedToolNames.ShouldBeNull();
    }

    // =====================================================================
    // Disabled / missing agents are not loaded (existing contract).
    // =====================================================================

    [Fact]
    public async Task ResolveAgent_DisabledAgent_ReturnsNull()
    {
        var agent = new Agent
        {
            Name = "disabled-child",
            Provider = "OpenAI",
            Model = "gpt-4o",
            IsEnabled = false
        };
        _context.Set<Agent>().Add(agent);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var executor = await ExecutionStrategyAgentLoader.ResolveAgentAsync(agent.Id, BuildContext(), CancellationToken.None);

        executor.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveAgent_MissingAgent_ReturnsNull()
    {
        var executor = await ExecutionStrategyAgentLoader.ResolveAgentAsync(Guid.NewGuid(), BuildContext(), CancellationToken.None);

        executor.ShouldBeNull();
    }
}

/// <summary>
/// Test-only DbContext - Agent + Provider + the three grant entities.
/// Mirrors AgentGrantWiringDbContext (no AgentVersion needed: the loader never writes versions).
/// </summary>
internal sealed class LoaderGrantDbContext : TnziDbContext<LoaderGrantDbContext>
{
    public LoaderGrantDbContext(
        DbContextOptions<LoaderGrantDbContext> options,
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
