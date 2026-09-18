namespace Tnzi.AI.Tests;

/// <summary>
/// 评估必须真的跑被评估的那个 Agent（含指定版本），且不在评估者名下留下线程。
/// </summary>
/// <remarks>
/// 2026-09-12 前 <see cref="DefaultAgentEvaluator"/> 发的是一条只有 Message 的裸聊天：AgentId / VersionNumber 只写进
/// <c>EvaluationRun</c> 行，用例经 AgentResolver 第三分支跑默认提供商的裸模型，系统提示 / 工具 / 人格一次都没参与，
/// 版本对比比较的是同一条默认路径的两份噪声；每个用例还在管理员名下新建一条 AgentThread。
/// </remarks>
public class EvaluationTargetsAgentTests
{
    private static IOptionsMonitor<AIOptions> CreateOptions() => new StaticOptionsMonitor<AIOptions>(new AIOptions
    {
        DefaultProvider = "OpenAI",
        Providers = new Dictionary<string, ProviderOptions>
        {
            ["OpenAI"] = new() { Enabled = true, ApiKey = "sk-test-12345678901234567890", DefaultModel = "gpt-4o" }
        }
    });

    [Fact]
    public async Task Evaluator_RunsTheEvaluatedAgentVersion_EphemerallyAsTheCurrentUser()
    {
        var agentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        AgentRunRequest? captured = null;
        var runtime = new Mock<IAgentDispatchFacade>();
        runtime.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new AgentRunResult { Response = "4", Status = AgentRunStatus.Completed });
        var aiUtility = new Mock<IAiUtility>();
        aiUtility.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(userId);

        var evaluator = new DefaultAgentEvaluator(
            runtime.Object, Mock.Of<IRepository<EvaluationRun, Guid>>(), aiUtility.Object,
            NullLogger<DefaultAgentEvaluator>.Instance, currentUser.Object);

        var result = await evaluator.EvaluateAsync(new EvaluationCase
        {
            Input = "2+2?", ExpectedOutput = "4", AgentId = agentId, VersionNumber = 3, Provider = "OpenAI", Model = "gpt-4o-mini"
        });

        result.Passed.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.AgentId.ShouldBe(agentId);
        captured.AgentVersionNumber.ShouldBe(3);
        captured.Provider.ShouldBe("OpenAI");
        captured.Model.ShouldBe("gpt-4o-mini");
        captured.UserId.ShouldBe(userId);
        captured.Ephemeral.ShouldBeTrue();
        captured.OperationType.ShouldBe(AIOperationType.Evaluation);
    }

    [Fact]
    public async Task EvaluationService_CreateAndRun_PropagatesAgentAndVersionToEveryCase()
    {
        var agentId = Guid.NewGuid();
        var seen = new List<EvaluationCase>();
        var evaluator = new Mock<IAgentEvaluator>();
        evaluator.Setup(e => e.EvaluateAsync(It.IsAny<EvaluationCase>(), It.IsAny<CancellationToken>()))
            .Callback<EvaluationCase, CancellationToken>((c, _) => seen.Add(c))
            .ReturnsAsync((EvaluationCase c, CancellationToken _) => new EvaluationResult { CaseId = c.CaseId, ActualOutput = "x", Passed = true, Score = 1 });
        var repository = new Mock<IRepository<EvaluationRun, Guid>>();
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        var service = new EvaluationService(services, repository.Object, evaluator.Object, Mock.Of<IServiceScopeFactory>());

        var result = await service.CreateAndRunAsync(new CreateEvaluationRunDto
        {
            AgentId = agentId,
            VersionNumber = 7,
            Cases = [new EvaluationCaseDto { Input = "a" }, new EvaluationCaseDto { Input = "b" }]
        });

        result.Succeeded.ShouldBeTrue();
        seen.Count.ShouldBe(2);
        seen.ShouldAllBe(c => c.AgentId == agentId && c.VersionNumber == 7);
    }

    [Fact]
    public async Task AgentResolver_PinnedVersion_RunsTheSnapshotNotTheLiveRow()
    {
        var agentId = Guid.NewGuid();
        var live = new Agent { Id = agentId, Name = "agent", Provider = "OpenAI", Model = "gpt-4o", IsEnabled = true, Instructions = "live instructions" };
        var snapshot = new Agent { Id = agentId, Name = "agent", Provider = "OpenAI", Model = "gpt-4o", IsEnabled = true, Instructions = "version 3 instructions" };

        var agentRepository = new Mock<IRepository<Agent, Guid>>();
        agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(live);
        var grantService = new Mock<IAgentGrantService>();
        grantService.Setup(s => s.GetGrantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AgentGrantsProjection());
        var versionRouter = new Mock<IAgentVersionRouter>();
        versionRouter.Setup(r => r.RouteToVersionAsync(live, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentVersionRouteResult { Agent = snapshot, SelectedVersion = 3, SnapshotGrants = new AgentGrantsProjection() });

        string? capturedInstructions = null;
        var factory = new Mock<IAgentFactory>();
        factory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IEnumerable<string>?>(),
                It.IsAny<double?>(), It.IsAny<int?>(), It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, IEnumerable<string>?, Guid?, CancellationToken>(
                (_, _, instructions, _, _, _, _, _, _, _, _, _) => capturedInstructions = instructions)
            .ReturnsAsync(new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "test" }));

        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest { AgentId = agentId, UserMessage = "q", AgentVersionNumber = 3 }
        };
        var resolver = new AgentResolver(
            factory.Object, CreateOptions(), agentRepository.Object,
            new UserToolPermissionResolver(new ToolRegistry(Mock.Of<ILogger<ToolRegistry>>()), Mock.Of<ILogger<UserToolPermissionResolver>>()),
            new SimplePromptTemplateEngine(), versionRouter.Object, grantService.Object, Mock.Of<ILogger<AgentResolver>>(),
            executionContextAccessor: accessor);

        var resolution = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        capturedInstructions.ShouldBe("version 3 instructions");
        versionRouter.Verify(r => r.RouteAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()), Times.Never,
            "a pinned version must not be re-routed by the A/B router");
    }

    [Fact]
    public async Task HistoryMiddleware_EphemeralRequest_CreatesNoThreadAndPersistsNothing()
    {
        var threadService = new Mock<IAgentThreadInternalService>(MockBehavior.Strict);
        var middleware = new HistoryMiddleware(threadService.Object, new StaticOptionsMonitor<AIOptions>(new AIOptions()), Mock.Of<ILogger<HistoryMiddleware>>());
        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "q", Ephemeral = true },
            Agent = new AgentResolution { Provider = "test", Model = "test-model" },
            ServiceProvider = Mock.Of<IServiceProvider>()
        };

        var result = await middleware.InvokeAsync(context, (_, _) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        result.Response.ShouldBe("ok");
        context.Request.ThreadId.ShouldBeNull();
        threadService.VerifyNoOtherCalls();
    }
}
