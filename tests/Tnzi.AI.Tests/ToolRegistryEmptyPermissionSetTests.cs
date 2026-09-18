using Tnzi.Security.Authorization;
using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests;

/// <summary>
/// 工具权限过滤对「空权限集」的失败方向：空集 = 该用户没有任何所需权限 ⇒ 门控工具必须被排除；
/// 只有 <c>null</c> 才表示调用方明确放弃门控。
/// </summary>
/// <remarks>
/// 此前三处实现把 <c>Count == 0</c> 与 <c>null</c> 一视同仁地当作「不过滤」：
/// <c>AgentResolver</c> 对一个一条所需权限都不持有的用户算出空集，注册表收到空集后返回全部工具 ——
/// 零权限用户拿到的工具比持部分权限的用户更多，且无日志无异常。
/// </remarks>
public class ToolRegistryEmptyPermissionSetTests
{
    private sealed class GatedTools : IAIToolProvider
    {
        public string OpenTool() => "open";
        public string RunPayroll() => "payroll";
    }

    private static ToolRegistry BuildRegistry()
    {
        var registry = new ToolRegistry(Mock.Of<ILogger<ToolRegistry>>());
        registry.Register(new ToolDefinition
        {
            Name = "open_tool",
            GroupName = "hr",
            ProviderType = typeof(GatedTools),
            MethodInfo = typeof(GatedTools).GetMethod(nameof(GatedTools.OpenTool))!,
            RequiredPermissions = []
        });
        registry.Register(new ToolDefinition
        {
            Name = "run_payroll",
            GroupName = "hr",
            ProviderType = typeof(GatedTools),
            MethodInfo = typeof(GatedTools).GetMethod(nameof(GatedTools.RunPayroll))!,
            RequiredPermissions = ["hr.payroll.execute"]
        });
        return registry;
    }

    [Fact]
    public void GetToolsByGroupsWithPermissions_EmptyPermissions_ExcludesGatedTools()
    {
        var registry = BuildRegistry();

        var result = registry.GetToolsByGroupsWithPermissions(["hr"], userPermissions: []);

        result.Select(t => t.Name).ShouldBe(["open_tool"]);
    }

    [Fact]
    public void GetToolsByNames_EmptyPermissions_ExcludesGatedTools()
    {
        var registry = BuildRegistry();

        var result = registry.GetToolsByNames(["open_tool", "run_payroll"], userPermissions: []);

        result.Select(t => t.Name).ShouldBe(["open_tool"]);
    }

    [Fact]
    public void GetToolsByNames_NullPermissions_SkipsFiltering()
    {
        // null 是「调用方明确放弃门控」（AgentResolver 收集所需权限并集时就靠它拿到全部工具），语义钉住。
        var registry = BuildRegistry();

        var result = registry.GetToolsByNames(["open_tool", "run_payroll"], userPermissions: null);

        result.Select(t => t.Name).OrderBy(x => x).ToArray().ShouldBe(["open_tool", "run_payroll"]);
    }

    [Fact]
    public void DefaultInterfaceGetToolsByNames_EmptyPermissions_ExcludesGatedTools()
    {
        // 接口默认实现（自定义注册表不重写时走这条）必须与 ToolRegistry 同口径。
        IToolRegistry registry = new AllToolsOnlyRegistry(BuildRegistry().GetAllTools());

        var result = registry.GetToolsByNames(["open_tool", "run_payroll"], userPermissions: []);

        result.Select(t => t.Name).ShouldBe(["open_tool"]);
    }

    [Fact]
    public async Task ResolveAgentAsync_UserHoldsNoneOfRequiredPermissions_PassesEmptySetNotNull()
    {
        IEnumerable<string>? captured = ["sentinel"];
        var agentFactory = CapturingAgentFactory(p => captured = p);

        var permissionChecker = new Mock<IPermissionChecker>();
        permissionChecker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);

        var resolver = BuildResolver(agentFactory, permissionChecker.Object, out var agentId);

        var resolution = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolveAgentAsync_NoPermissionChecker_GatedToolsRequested_PassesEmptySetNotNull()
    {
        // 未加载 Authorization 而 Agent 引用了门控工具：失败关闭（空集 ⇒ 门控工具被排除），不是放行全部。
        IEnumerable<string>? captured = ["sentinel"];
        var agentFactory = CapturingAgentFactory(p => captured = p);

        var resolver = BuildResolver(agentFactory, permissionChecker: null, out var agentId);

        var resolution = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolveAgentAsync_UsesTheRequestUserIdForPermissionChecks_NotTheAmbientUser()
    {
        // 后台子 Agent 在新作用域里跑，环境里没有当前用户：按请求携带的 UserId 检查
        // （与 ApprovalToolWrapper / 权限规则评估同源），否则 spawn 出来的运行一律丢掉全部门控工具。
        var userId = Guid.NewGuid();
        IEnumerable<string>? captured = ["sentinel"];
        var agentFactory = CapturingAgentFactory(p => captured = p);

        var permissionChecker = new Mock<IPermissionChecker>();
        permissionChecker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);
        permissionChecker.Setup(p => p.IsGrantedAsync(userId, "hr.payroll.execute")).ReturnsAsync(true);

        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest { UserMessage = "hi", UserId = userId }
        };
        var resolver = BuildResolver(agentFactory, permissionChecker.Object, out var agentId, accessor);

        var resolution = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured.ShouldBe(["hr.payroll.execute"]);
        permissionChecker.Verify(p => p.IsGrantedAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAgentAsync_NoRequestUserId_FallsBackToTheAmbientUser()
    {
        IEnumerable<string>? captured = ["sentinel"];
        var agentFactory = CapturingAgentFactory(p => captured = p);

        var permissionChecker = new Mock<IPermissionChecker>();
        permissionChecker.Setup(p => p.IsGrantedAsync("hr.payroll.execute")).ReturnsAsync(true);

        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest { UserMessage = "hi" }
        };
        var resolver = BuildResolver(agentFactory, permissionChecker.Object, out var agentId, accessor);

        var resolution = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        captured.ShouldBe(["hr.payroll.execute"]);
    }

    [Fact]
    public async Task ToolResolver_EmptyPermissions_EndToEnd_GatedToolAbsent()
    {
        // 走真实 ToolRegistry + 真实 ToolResolver：空集一路传到执行器的工具清单里，门控工具不能出现。
        var registry = BuildRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.None));
        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions());
        var resolver = new ToolResolver(
            registry,
            Mock.Of<IMcpToolProvider>(),
            new OpenApiToolGenerator(Mock.Of<IHttpClientFactory>(), options, loggerFactory.CreateLogger<OpenApiToolGenerator>()),
            options,
            services,
            loggerFactory,
            loggerFactory.CreateLogger<ToolResolver>());

        var tools = await resolver.ResolveToolsAsync(["hr"], userPermissions: [], toolNames: ["run_payroll"]);

        tools.ShouldNotBeNull();
        tools.Select(t => t.Name).ShouldNotContain("run_payroll");
    }

    private static Mock<IAgentFactory> CapturingAgentFactory(Action<IEnumerable<string>?> capture)
    {
        var agentFactory = new Mock<IAgentFactory>();
        agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<double?>(),
                It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, IEnumerable<string>?, Guid?, CancellationToken>(
                (_, _, _, _, _, _, _, _, userPermissions, _, _, _) => capture(userPermissions))
            .ReturnsAsync(new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "test" }));
        return agentFactory;
    }

    private static AgentResolver BuildResolver(Mock<IAgentFactory> agentFactory, IPermissionChecker? permissionChecker, out Guid agentId,
        IAgentExecutionContextAccessor? executionContextAccessor = null)
    {
        agentId = Guid.NewGuid();
        var id = agentId;

        var agentRepository = new Mock<IRepository<Agent, Guid>>();
        agentRepository.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent
            {
                Id = id,
                Name = "agent",
                Provider = "OpenAI",
                Model = "gpt-4o",
                IsEnabled = true
            });

        var aiOptions = new StaticOptionsMonitor<AIOptions>(new AIOptions
        {
            DefaultProvider = "OpenAI",
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["OpenAI"] = new() { Enabled = true, ApiKey = "sk-test-12345678901234567890", DefaultModel = "gpt-4o" }
            }
        });

        var versionRouter = new Mock<IAgentVersionRouter>();
        versionRouter.Setup(r => r.RouteAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent a, CancellationToken _) => AgentVersionRouteResult.Passthrough(a));

        var grantService = new Mock<IAgentGrantService>();
        grantService.Setup(s => s.GetGrantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentGrantsProjection { ToolGroups = ["hr"] });

        return new AgentResolver(
            agentFactory.Object,
            aiOptions,
            agentRepository.Object,
            new UserToolPermissionResolver(BuildRegistry(), Mock.Of<ILogger<UserToolPermissionResolver>>(), permissionChecker, executionContextAccessor),
            new SimplePromptTemplateEngine(),
            versionRouter.Object,
            grantService.Object,
            Mock.Of<ILogger<AgentResolver>>());
    }

    /// <summary>
    /// 只实现 <see cref="IToolRegistry.GetAllTools"/> 的最小注册表，用来执行接口的默认 <c>GetToolsByNames</c>。
    /// </summary>
    private sealed class AllToolsOnlyRegistry(IReadOnlyList<ToolDefinition> tools) : IToolRegistry
    {
        public IReadOnlyList<ToolDefinition> GetAllTools() => tools;
        public IReadOnlyList<ToolDefinition> GetToolsByGroup(string groupName) => throw new NotSupportedException();
        public IReadOnlyList<ToolDefinition> GetToolsByGroups(IEnumerable<string> groupNames) => throw new NotSupportedException();
        public IEnumerable<string> GetAllGroupNames() => throw new NotSupportedException();
        public IReadOnlyList<ToolDefinition> GetToolsByGroupsWithPermissions(IEnumerable<string> groupNames, IEnumerable<string>? userPermissions = null) => throw new NotSupportedException();
        public void Register(ToolDefinition tool) => throw new NotSupportedException();
    }
}
