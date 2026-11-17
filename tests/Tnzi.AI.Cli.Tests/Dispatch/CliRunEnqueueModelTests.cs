namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 入队请求里的模型是用户可以自填的输入，调度器按用户被允许的模型校验它。
/// </summary>
/// <remarks>
/// 用户端 <c>POST cli-runs</c> 把请求体原样交给调度器，模型又会以 <c>--model</c> 交给 CLI 并盖掉绑定上配的那个：
/// 不校验的话，持有 <c>ai.agent.execute</c> 的用户点名哪个模型就用哪个，聊天入口的允许列表从旁边就绕过去了。
/// 每条「不采纳」都配一条「采纳」的对照，否则把字段整体丢掉也一样绿。
/// </remarks>
public class CliRunEnqueueModelTests : IntegratedTestBase<CliThreadOwnershipDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111c1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222c2");
    private static readonly Guid CodexRuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222c3");

    /// <summary>落点（每个测试自己设；null = 不注册落点解析器）。</summary>
    private CliRunPlacement? _placement;
    private CliRunPlacementRequest? _placementRequest;
    private Dictionary<string, JsonElement>? _metadata;
    private const string Sonnet = "claude-sonnet-5-5";
    private const string Opus = "claude-opus-5-5";

    // 字段初始化先于基类构造函数执行，ConfigureServices 里就能用到它们。
    private readonly Mock<IPermissionChecker> _permissionChecker = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    // CallBase：调度器调的是 ResolveChatTurnAsync，它的默认实现转到下面设好的 ResolveChatModelAsync（Moq 默认不执行接口的默认成员）
    private readonly Mock<IUsagePolicyService> _usagePolicy = new() { CallBase = true };

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRuntime, Guid>>();
        services.AddScoped<IRepository<AgentThread, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, AgentThread, Guid>>();

        _permissionChecker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);
        services.AddScoped(_ => _permissionChecker.Object);

        SignIn(TestHelper.DefaultTestUserId);
        services.AddScoped(_ => _currentUser.Object);

        _usagePolicy.Setup(p => p.ResolveChatModelAsync(It.IsAny<Guid?>(), AgentId, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid? _, Guid? _, string? _, string? model, CancellationToken _) =>
                string.Equals(model, Sonnet, StringComparison.OrdinalIgnoreCase)
                    ? Result<string?>.Success(Sonnet)
                    : Result<string?>.Failure($"The model '{model}' is not available to you for this assistant.", 403, ErrorCodes.ModelNotAllowed));
    }

    [Fact]
    public async Task ASignedInUser_IsRefusedAModelTheyMayNotUse_AndNothingIsQueued()
    {
        await SeedBindingAsync();

        var result = await EnqueueAsync(Opus, withModelList: true);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.ModelNotAllowed);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ASignedInUser_GetsTheModelThePolicyResolved()
    {
        await SeedBindingAsync();

        var run = await EnqueuedRunAsync("CLAUDE-SONNET-5-5", withModelList: true);

        run.Model.ShouldBe(Sonnet);
        _usagePolicy.Verify(p => p.ResolveChatModelAsync(TestHelper.DefaultTestUserId, AgentId, null, "CLAUDE-SONNET-5-5", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithoutAModelList_ASignedInUsersModelIsIgnored()
    {
        // 与聊天门面同一口径：没有清单就没有允许列表，放开它等于任何人都能点名最贵的那个
        await SeedBindingAsync();

        var run = await EnqueuedRunAsync(Opus, withModelList: false);

        run.Model.ShouldBeNull();
        _usagePolicy.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ACallerWhoControlsCliRuns_SetsTheModel()
    {
        await SeedBindingAsync();
        _permissionChecker.Setup(p => p.IsGrantedAsync(CliPermissions.CliRunExecute)).ReturnsAsync(true);

        var run = await EnqueuedRunAsync($" {Opus} ", withModelList: false);

        run.Model.ShouldBe(Opus);
    }

    [Fact]
    public async Task AnInProcessCallerWithoutASignedInUser_SetsTheModel()
    {
        await SeedBindingAsync();
        SignIn(userId: null);

        var run = await EnqueuedRunAsync(Opus, withModelList: true);

        run.Model.ShouldBe(Opus);
        _usagePolicy.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NoRequestedModel_LeavesItToTheBinding()
    {
        await SeedBindingAsync();

        var run = await EnqueuedRunAsync(model: null, withModelList: true);

        run.Model.ShouldBeNull();
        _usagePolicy.VerifyNoOtherCalls();
    }

    // ── 强度 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASignedInUser_GetsTheEffortThePolicyResolved_OnTheModelItWasCheckedAgainst()
    {
        await SeedBindingAsync();
        _usagePolicy.Setup(p => p.ResolveChatTurnAsync(TestHelper.DefaultTestUserId, AgentId, null, null, "HIGH", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatTurnModel>.Success(new ChatTurnModel(Sonnet, "high")));

        var run = await EnqueuedRunAsync(model: null, withModelList: true, effort: "HIGH");

        run.Effort.ShouldBe("high");
        run.Model.ShouldBe(Sonnet, "an effort belongs to a model: the run uses the model it was checked against");
    }

    [Fact]
    public async Task ASignedInUser_IsRefusedAnEffortTheModelDoesNotOffer_AndNothingIsQueued()
    {
        await SeedBindingAsync();
        _usagePolicy.Setup(p => p.ResolveChatTurnAsync(It.IsAny<Guid?>(), AgentId, null, Sonnet, "max", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatTurnModel>.Failure("no max", 403, ErrorCodes.EffortNotAllowed));

        var result = await EnqueueAsync(Sonnet, withModelList: true, effort: "max");

        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.EffortNotAllowed);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task WithoutAModelList_ASignedInUsersEffortIsRefused()
    {
        await SeedBindingAsync();

        var result = await EnqueueAsync(model: null, withModelList: false, effort: "high");

        result.ErrorCode.ShouldBe(ErrorCodes.EffortNotAllowed);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ATrustedCaller_SetsTheEffort()
    {
        await SeedBindingAsync();
        SignIn(userId: null);

        var run = await EnqueuedRunAsync(model: null, withModelList: false, effort: " xhigh ");

        run.Effort.ShouldBe("xhigh");
        run.Model.ShouldBeNull();
        _usagePolicy.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NoRequestedEffort_LeavesItToTheBinding()
    {
        await SeedBindingAsync();

        var run = await EnqueuedRunAsync(Sonnet, withModelList: true);

        run.Effort.ShouldBeNull();
    }

    // ── 落点换了 provider 或模型 ───────────────────────────────────────────────

    private void CodexOffers(params string[] efforts)
        => _usagePolicy.Setup(p => p.GetModelEffortsAsync(It.IsAny<Guid?>(), "codex", It.IsAny<string?>(), CodexRuntimeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(efforts);

    [Fact]
    public async Task AnEffortTheNewProviderDoesNotOffer_IsDropped_AndTheTurnStillQueues()
    {
        await SeedBindingAsync();
        SignIn(userId: null);
        CodexOffers("low", "medium", "high");
        _placement = new CliRunPlacement { RuntimeId = CodexRuntimeId };

        var run = await EnqueuedRunAsync(model: null, withModelList: true, effort: "xhigh");

        run.CliRuntimeId.ShouldBe(CodexRuntimeId);
        run.Effort.ShouldBeNull("codex does not take Claude's xhigh: the turn runs at codex's default instead of failing");
        _placementRequest!.RequestedEffort.ShouldBe("xhigh");
    }

    [Fact]
    public async Task AnEffortTheNewProviderOffers_IsKept_InItsSpelling()
    {
        await SeedBindingAsync();
        SignIn(userId: null);
        CodexOffers("low", "medium", "high");
        _placement = new CliRunPlacement { RuntimeId = CodexRuntimeId };

        var run = await EnqueuedRunAsync(model: null, withModelList: true, effort: "HIGH");

        run.Effort.ShouldBe("high");
    }

    [Fact]
    public async Task ThePlacementsOwnEffort_Wins()
    {
        await SeedBindingAsync();
        SignIn(userId: null);
        CodexOffers("low", "medium", "high");
        _placement = new CliRunPlacement { RuntimeId = CodexRuntimeId, Effort = " medium " };

        var run = await EnqueuedRunAsync(model: null, withModelList: true, effort: "max");

        run.Effort.ShouldBe("medium");
    }

    [Fact]
    public async Task APlacedModel_RechecksTheEffort_OnTheSameProvider()
    {
        await SeedBindingAsync();
        SignIn(userId: null);
        _usagePolicy.Setup(p => p.GetModelEffortsAsync(It.IsAny<Guid?>(), "claude", Sonnet, RuntimeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["low", "medium", "high"]);
        _placement = new CliRunPlacement { Model = Sonnet };

        var run = await EnqueuedRunAsync(Opus, withModelList: true, effort: "max");

        run.Model.ShouldBe(Sonnet);
        run.Effort.ShouldBeNull("max was asked of Opus; the placement runs Sonnet, which has none");
    }

    [Fact]
    public async Task TheTurnsMetadata_IsCheckedWith_AndReachesThePlacement()
    {
        await SeedBindingAsync();
        ChatModelsRequest? checkedWith = null;
        _usagePolicy.Setup(p => p.ResolveChatTurnAsync(It.IsAny<ChatModelsRequest>(), null, "on", It.IsAny<CancellationToken>()))
            .Callback((ChatModelsRequest r, string? _, string? _, CancellationToken _) => checkedWith = r)
            .ReturnsAsync(Result<ChatTurnModel>.Success(new ChatTurnModel("kimi-k2", "on")));
        _placement = new CliRunPlacement { WorkDirectory = "/work/x" };
        _metadata = new Dictionary<string, JsonElement> { ["cliProfile"] = JsonSerializer.SerializeToElement("kimi") };

        var run = await EnqueuedRunAsync(model: null, withModelList: true, effort: "on");

        run.Effort.ShouldBe("on");
        checkedWith!.Metadata!["cliProfile"].GetString().ShouldBe("kimi");
        _placementRequest!.Metadata["cliProfile"].GetString().ShouldBe("kimi");
    }

    [Fact]
    public async Task APlacementThatChangesNothing_KeepsTheEffort()
    {
        await SeedBindingAsync();
        SignIn(userId: null);
        _placement = new CliRunPlacement { WorkDirectory = "/work/x" };

        var run = await EnqueuedRunAsync(model: null, withModelList: true, effort: "xhigh");

        run.Effort.ShouldBe("xhigh");
        _usagePolicy.Verify(p => p.GetModelEffortsAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void SignIn(Guid? userId)
    {
        _currentUser.Setup(m => m.Id).Returns(userId);
        _currentUser.Setup(m => m.IsAuthenticated).Returns(userId.HasValue);
    }

    private async Task<CliRun> EnqueuedRunAsync(string? model, bool withModelList, string? effort = null)
    {
        var result = await EnqueueAsync(model, withModelList, effort);
        result.Succeeded.ShouldBeTrue(result.Message);

        return await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == result.Data);
    }

    private async Task<Result<Guid>> EnqueueAsync(string? model, bool withModelList, string? effort = null)
    {
        var dispatcher = new CliAgentDispatcher(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<AgentThread, Guid>>(),
            TestAgents.Repository(AgentId),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            EnabledOptions(),
            ServiceProvider,
            chatOptions: ChatOptions(withModelList),
            usagePolicy: _usagePolicy.Object,
            placementResolver: _placement is null ? null : new FakePlacement(this));

        return await dispatcher.EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x", Model = model, Effort = effort, Metadata = _metadata });
    }

    private sealed class FakePlacement(CliRunEnqueueModelTests owner) : ICliRunPlacementResolver
    {
        public Task<CliRunPlacement?> ResolveAsync(CliRunPlacementRequest request, CancellationToken cancellationToken)
        {
            owner._placementRequest = request;
            return Task.FromResult(owner._placement);
        }
    }

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }

    private static IOptionsMonitor<AiChatOptions> ChatOptions(bool withModelList)
    {
        var options = new AiChatOptions();
        if (withModelList)
        {
            options.Models = [new ChatModelOption { Id = Sonnet, Provider = "claude" }, new ChatModelOption { Id = Opus, Provider = "claude" }];
        }

        var monitor = new Mock<IOptionsMonitor<AiChatOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return monitor.Object;
    }

    private async Task SeedBindingAsync()
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            WorkDirectoryMode = CliWorkDirectoryMode.PerThread
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = "/usr/bin/claude",
            Status = CliRuntimeStatus.Online
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = CodexRuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "codex",
            Name = "codex @ TEST-HOST",
            ExecutablePath = "/usr/bin/codex",
            Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }
}
