using Microsoft.Data.Sqlite;
using Tnzi.AI.Tools.Models;
using Tnzi.Security.Authorization;

namespace Tnzi.AI.Tests;

/// <summary>
/// Handoff / Router / AgentAsTools 的子 agent 与主路径必须走同一道 RequiredPermissions 门：
/// 此前 <see cref="ExecutionStrategyAgentLoader"/> 从不传 userPermissions（null = 放弃门控），
/// 一个没有 <c>ai.tools.sandbox</c> 的用户对着一个 Router 父 agent 说话，
/// 被授予 sandbox 组的子 agent 就带着宿主 shell 工具替他跑。
/// </summary>
public class ExecutionStrategyAgentLoaderPermissionTests : IDisposable
{
    private sealed class GatedTools : IAIToolProvider
    {
        public string OpenTool() => "open";
        public string RunShell() => "shell";
    }

    private readonly SqliteConnection _connection;
    private readonly LoaderGrantDbContext _context;
    private readonly EFCoreRepository<LoaderGrantDbContext, Agent, Guid> _agentRepo;
    private readonly AgentGrantService _grantService;
    private readonly Mock<IAgentFactory> _agentFactory = new();

    /// <summary>工厂收到的 userPermissions；哨兵值用来区分「收到 null」与「根本没被调用」。</summary>
    private IEnumerable<string>? _capturedUserPermissions = ["sentinel"];

    public ExecutionStrategyAgentLoaderPermissionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);

        var options = new DbContextOptionsBuilder<LoaderGrantDbContext>().UseSqlite(_connection).Options;
        _context = new LoaderGrantDbContext(options, currentUserMock.Object);
        _context.Database.EnsureCreated();

        var grantSp = new ServiceCollection().AddLogging().BuildServiceProvider();
        _grantService = new AgentGrantService(
            grantSp,
            new EFCoreRepository<LoaderGrantDbContext, AgentToolGrant, Guid>(_context),
            new EFCoreRepository<LoaderGrantDbContext, AgentSkillGrant, Guid>(_context),
            new EFCoreRepository<LoaderGrantDbContext, AgentKnowledgeGrant, Guid>(_context));
        _agentRepo = new EFCoreRepository<LoaderGrantDbContext, Agent, Guid>(_context);

        _agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, IEnumerable<string>?, Guid?, CancellationToken>(
                (_, _, _, _, _, _, _, _, userPermissions, _, _, _) => _capturedUserPermissions = userPermissions?.ToList())
            .ReturnsAsync(new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "stub" }));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ToolRegistry BuildRegistry()
    {
        var registry = new ToolRegistry(Mock.Of<ILogger<ToolRegistry>>());
        registry.Register(new ToolDefinition
        {
            Name = "open_tool",
            GroupName = "sandbox",
            ProviderType = typeof(GatedTools),
            MethodInfo = typeof(GatedTools).GetMethod(nameof(GatedTools.OpenTool))!,
            RequiredPermissions = []
        });
        registry.Register(new ToolDefinition
        {
            Name = "run_shell",
            GroupName = "sandbox",
            ProviderType = typeof(GatedTools),
            MethodInfo = typeof(GatedTools).GetMethod(nameof(GatedTools.RunShell))!,
            RequiredPermissions = ["ai.tools.sandbox"]
        });
        return registry;
    }

    private ExecutionStrategyContext BuildContext(IPermissionChecker? permissionChecker, IAgentExecutionContextAccessor? accessor = null)
    {
        var permissionResolver = new UserToolPermissionResolver(
            BuildRegistry(), Mock.Of<ILogger<UserToolPermissionResolver>>(), permissionChecker, accessor);
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IAgentGrantService>(_grantService)
            .AddSingleton<IUserToolPermissionResolver>(permissionResolver)
            .BuildServiceProvider();
        return new ExecutionStrategyContext
        {
            AgentFactory = _agentFactory.Object,
            AgentRepository = _agentRepo,
            ServiceProvider = serviceProvider,
            ExecutionContextAccessor = accessor,
            Logger = NullLogger.Instance
        };
    }

    private async Task<Guid> SeedChildAgentAsync(params string[] groups)
    {
        var agent = new Agent { Name = "child", Provider = "OpenAI", Model = "gpt-4o", IsEnabled = true };
        _context.Set<Agent>().Add(agent);
        foreach (var group in groups)
        {
            _context.Set<AgentToolGrant>().Add(new AgentToolGrant
            {
                AgentId = agent.Id, GrantType = GrantType.Group, ToolKey = group, IsEnabled = true, Priority = 0
            });
        }
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return agent.Id;
    }

    [Fact]
    public async Task ResolveAgent_ChildWithGatedGroup_UserHoldsNothing_PassesEmptySetNotNull()
    {
        var agentId = await SeedChildAgentAsync("sandbox");
        var checker = new Mock<IPermissionChecker>();
        checker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);

        var executor = await ExecutionStrategyAgentLoader.ResolveAgentAsync(agentId, BuildContext(checker.Object), CancellationToken.None);

        executor.ShouldNotBeNull();
        // null 会让注册表放行全部门控工具；空集才是「一条都不持有」。
        _capturedUserPermissions.ShouldNotBeNull();
        _capturedUserPermissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolveAgent_ChildWithGatedGroup_UserHoldsPermission_PassesGrantedSubset()
    {
        var userId = Guid.NewGuid();
        var agentId = await SeedChildAgentAsync("sandbox");
        var checker = new Mock<IPermissionChecker>();
        checker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);
        checker.Setup(p => p.IsGrantedAsync(userId, "ai.tools.sandbox")).ReturnsAsync(true);
        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest { UserMessage = "hi", UserId = userId }
        };

        await ExecutionStrategyAgentLoader.ResolveAgentAsync(agentId, BuildContext(checker.Object, accessor), CancellationToken.None);

        // 与主路径同源：按请求携带的 UserId 检查，不按环境用户。
        _capturedUserPermissions.ShouldBe(["ai.tools.sandbox"]);
        checker.Verify(p => p.IsGrantedAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAgent_ChildWithGatedGroup_NoPermissionChecker_FailsClosed()
    {
        var agentId = await SeedChildAgentAsync("sandbox");

        await ExecutionStrategyAgentLoader.ResolveAgentAsync(agentId, BuildContext(permissionChecker: null), CancellationToken.None);

        _capturedUserPermissions.ShouldNotBeNull();
        _capturedUserPermissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolveAgent_ChildWithoutGatedTools_PassesNull()
    {
        // 没有任何工具声明权限 ⇒ 不门控（null），与主路径逐字相同。
        var agentId = await SeedChildAgentAsync("fs");
        var checker = new Mock<IPermissionChecker>();

        await ExecutionStrategyAgentLoader.ResolveAgentAsync(agentId, BuildContext(checker.Object), CancellationToken.None);

        _capturedUserPermissions.ShouldBeNull();
        checker.Verify(p => p.IsGrantedAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAgent_EndToEnd_GatedToolAbsentFromChildExecutor()
    {
        // 真实 AgentFactory + 真实 ToolResolver：门控工具不能出现在子 agent 的工具清单里。
        var agentId = await SeedChildAgentAsync("sandbox");
        var checker = new Mock<IPermissionChecker>();
        checker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);

        var registry = BuildRegistry();
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.None));
        var aiOptions = new StaticOptionsMonitor<AIOptions>(new AIOptions
        {
            DefaultProvider = "OpenAI",
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["OpenAI"] = new() { Enabled = true, ApiKey = "sk-test-12345678901234567890", DefaultModel = "gpt-4o" }
            }
        });
        var services = new ServiceCollection().BuildServiceProvider();
        var toolResolver = new ToolResolver(
            registry,
            Mock.Of<IMcpToolProvider>(),
            new OpenApiToolGenerator(Mock.Of<IHttpClientFactory>(), aiOptions, loggerFactory.CreateLogger<OpenApiToolGenerator>()),
            aiOptions,
            services,
            loggerFactory,
            loggerFactory.CreateLogger<ToolResolver>());
        var chatClientFactory = new Mock<IChatClientFactory>();
        chatClientFactory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>())).Returns(Mock.Of<IChatClient>());
        var optionsBuilder = new AgentExecutorOptionsBuilder(
            aiOptions, loggerFactory, chatClientFactory.Object, middlewares: [],
            tokenEstimator: new HeuristicTokenEstimator(), logger: NullLogger<AgentExecutorOptionsBuilder>.Instance);
        var factory = new AgentFactory(chatClientFactory.Object, aiOptions, toolResolver, optionsBuilder, registry, NullLogger<AgentFactory>.Instance);

        var permissionResolver = new UserToolPermissionResolver(registry, Mock.Of<ILogger<UserToolPermissionResolver>>(), checker.Object);
        var context = new ExecutionStrategyContext
        {
            AgentFactory = factory,
            AgentRepository = _agentRepo,
            ServiceProvider = new ServiceCollection()
                .AddSingleton<IAgentGrantService>(_grantService)
                .AddSingleton<IUserToolPermissionResolver>(permissionResolver)
                .BuildServiceProvider(),
            Logger = NullLogger.Instance
        };

        var executor = await ExecutionStrategyAgentLoader.ResolveAgentAsync(agentId, context, CancellationToken.None);

        executor.ShouldNotBeNull();
        var names = ((AgentExecutor)executor).Tools.Select(t => t.Name).ToList();
        names.ShouldContain("open_tool");
        names.ShouldNotContain("run_shell");
    }
}
