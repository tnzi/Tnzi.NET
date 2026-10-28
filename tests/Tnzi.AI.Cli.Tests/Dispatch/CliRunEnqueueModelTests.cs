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
    private const string Sonnet = "claude-sonnet-5-5";
    private const string Opus = "claude-opus-5-5";

    // 字段初始化先于基类构造函数执行，ConfigureServices 里就能用到它们。
    private readonly Mock<IPermissionChecker> _permissionChecker = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUsagePolicyService> _usagePolicy = new();

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

    // ── helpers ──────────────────────────────────────────────────────────────

    private void SignIn(Guid? userId)
    {
        _currentUser.Setup(m => m.Id).Returns(userId);
        _currentUser.Setup(m => m.IsAuthenticated).Returns(userId.HasValue);
    }

    private async Task<CliRun> EnqueuedRunAsync(string? model, bool withModelList)
    {
        var result = await EnqueueAsync(model, withModelList);
        result.Succeeded.ShouldBeTrue(result.Message);

        return await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == result.Data);
    }

    private async Task<Result<Guid>> EnqueueAsync(string? model, bool withModelList)
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
            usagePolicy: _usagePolicy.Object);

        return await dispatcher.EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x", Model = model });
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
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }
}
