namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 运行时按「执行目标 × CLI」登记、按副本服务的目标认领、目标不在线时请使用方唤起、按运行指定工作目录。
/// </summary>
/// <remarks>
/// 真实的队列处理器（认领、在线刷新、唤起）、执行器、工作区布置器、远程运行时登记与 stream-json 适配器，SQLite 仓储；
/// 执行宿主是 <see cref="TestCliHost"/>（内存里的 POSIX 文件系统），它的覆盖范围在测试里切换，模拟不同的服务端副本。
/// </remarks>
public class WorkspaceRuntimeTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111e1");
    private static readonly Guid BindingRuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222e2");
    private static readonly Guid ThreadId = Guid.Parse("33333333-3333-3333-3333-3333333333e3");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private const string Alice = "ws-alice";
    private const string Bob = "ws-bob";
    private const string RemoteRoot = "/home/alice/.tnzi/workspaces";
    private const string ChatRoot = "/home/alice/chat";
    private const string RemoteExecutable = "/usr/local/bin/claude";

    private readonly string _configuredLocalRoot = TestScratch.NewPath("placement-cfg");
    private readonly Mock<ICliTargetActivator> _activator = new();
    private TestCliHost _host = TestCliHost.Starting(() => throw new InvalidOperationException("No process was expected."));

    public WorkspaceRuntimeTests()
    {
        // 登记结果按运行时 DTO 投影
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliBudgetDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliBudgetDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliBudgetDbContext, CliRuntime, Guid>>();

        // 队列处理器每轮从作用域里取执行宿主：测试切换 _host 就是换了一个服务端副本
        services.AddTransient<ICliHost>(_ => _host);
        services.AddScoped(_ => _activator.Object);
    }

    public override void Dispose()
    {
        TestScratch.Delete(_configuredLocalRoot);
        base.Dispose();
    }

    // ── 认领：只认领执行得了的 ─────────────────────────────────────────────────

    [Fact]
    public async Task AReplicaDoesNotClaimARunThatOnlyAnotherWorkspaceCanExecute()
    {
        var bobs = await SeedRemoteRuntimeAsync(Bob, online: true);
        var run = await SeedQueuedRunAsync(bobs);
        var processor = CreateProcessor();

        _host.Coverage = CliHostCoverage.InProcessAnd([Alice]);
        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBeNull("this replica serves Alice's workspace, not Bob's");
        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Queued);

        _host.Coverage = CliHostCoverage.RemoteOnly([Bob]);
        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBe(run.Id);
    }

    [Fact]
    public async Task RunsOnServerLocalRuntimes_AreClaimedByReplicasThatRunLocalClis_AsBefore()
    {
        await SeedBindingAsync();
        var run = await SeedQueuedRunAsync(BindingRuntimeId);
        var processor = CreateProcessor();

        _host.Coverage = CliHostCoverage.RemoteOnly([Alice]);
        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBeNull("this replica runs no CLI of its own");

        _host.Coverage = CliHostCoverage.InProcess;
        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBe(run.Id);
    }

    [Fact]
    public async Task ADetachedRun_IsReattachedByTheReplicaServingItsWorkspace_NotByAnother()
    {
        var bobs = await SeedRemoteRuntimeAsync(Bob, online: true);
        var run = await SeedDetachedRunAsync(bobs, DateTime.UtcNow.AddMinutes(-1));
        var processor = CreateProcessor();

        _host.Coverage = CliHostCoverage.InProcessAnd([Alice]);
        (await processor.TryClaimReattachAsync(CancellationToken.None)).ShouldBeNull();

        _host.Coverage = CliHostCoverage.RemoteOnly([Bob]);
        (await processor.TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id);
    }

    [Fact]
    public async Task ADetachedRunWhoseWorkspaceNeverCameBack_IsInterruptedByAnyReplica_WithoutAskingAHost()
    {
        var bobs = await SeedRemoteRuntimeAsync(Bob, online: false);
        var run = await SeedDetachedRunAsync(bobs, DateTime.UtcNow.AddMinutes(-20));
        _host = RemoteHost(Alice);
        var processor = CreateProcessor();

        (await processor.TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id,
            "nobody can reattach it any more, so any replica may end it rather than leave it Running forever");
        await CreateExecutor().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var ended = await ReloadAsync(run.Id);
        ended.Status.ShouldBe(CliRunStatus.Failed);
        ended.FailureReason.ShouldBe(CliRunFailureReason.Interrupted);
        ended.Error.ShouldNotBeNull().ShouldContain(Bob);
        _host.Reattaches.ShouldBeEmpty("Alice's replica must not ask its host for Bob's process");
        _host.Scopes.ShouldBeEmpty();
    }

    // ── 不在线：唤起，上线后执行 ──────────────────────────────────────────────

    [Fact]
    public async Task AnOfflineWorkspace_IsActivatedOnce_AndTheRunExecutesThereOnceItComesOnline()
    {
        await SeedBindingAsync();
        var runtimeId = await SeedRemoteRuntimeAsync(Alice, online: false, version: null);
        var workDirectory = $"{ChatRoot}/{ThreadId:N}";
        var run = await SeedQueuedRunAsync(runtimeId, workDirectory, create: true);
        _activator.Setup(a => a.ActivateAsync(It.IsAny<CliActivationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CliActivationResult.Started);

        var processor = CreateProcessor();
        _host = RemoteHost(Alice);
        _host.Coverage = CliHostCoverage.InProcess;

        // 没有副本服务 Alice 的工作区：没人认领，框架请使用方把它起来，同一个间隔里只问一次
        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBeNull();
        await processor.ActivateWaitingTargetsAsync(CancellationToken.None);
        await processor.ActivateWaitingTargetsAsync(CancellationToken.None);
        _activator.Verify(a => a.ActivateAsync(
            It.Is<CliActivationRequest>(r => r.TargetId == Alice && r.RuntimeId == runtimeId && r.RunId == run.Id && r.ThreadId == ThreadId),
            It.IsAny<CancellationToken>()), Times.Once);

        // 工作区起来了：它的代理登记 CLI，这个副本开始服务它
        var registration = await CreateRegistry().RegisterAsync(new CliTargetRegistration
        {
            TargetId = Alice,
            Runtimes = [new CliTargetRuntime { ProviderKey = "claude", ExecutablePath = RemoteExecutable, Version = "2.1.310 (Claude Code)" }]
        });
        registration.Runtimes.ShouldHaveSingleItem().Id.ShouldBe(runtimeId, "the placeholder row is the one that gets registered");
        _host.Coverage = CliHostCoverage.InProcessAnd([Alice]);
        await processor.RefreshCoverageAsync(CancellationToken.None);

        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBe(run.Id);
        await CreateExecutor().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var finished = await ReloadAsync(run.Id);
        finished.Status.ShouldBe(CliRunStatus.Completed, finished.Error);
        finished.WorkDirectory.ShouldBe(workDirectory);
        _host.Starts.Single(s => s.Purpose == CliProcessPurpose.AgentSession).WorkingDirectory.ShouldBe(workDirectory);
        _host.Scopes.ShouldAllBe(s => s.TargetId == Alice && s.RuntimeMode == CliRuntimeMode.RemoteDaemon);

        // 目录是框架按要求建出来的，里面一个受管文件都没有
        (await _host.FileSystem.DirectoryExistsAsync(workDirectory, CancellationToken.None)).ShouldBeTrue();
        FilesUnder(workDirectory).ShouldBeEmpty();

        var runtime = await DbContext.Set<CliRuntime>().AsNoTracking().SingleAsync(r => r.Id == runtimeId);
        runtime.Status.ShouldBe(CliRuntimeStatus.Online);
        runtime.CliVersion.ShouldBe("2.1.310 (Claude Code)");
        runtime.ActivationStartedAt.ShouldBeNull();
    }

    [Fact]
    public async Task AWorkspaceThatDoesNotComeOnlineInTime_EndsItsQueuedRunsAsTargetUnavailable()
    {
        var runtimeId = await SeedRemoteRuntimeAsync(Alice, online: false, version: null,
            activationStartedAt: DateTime.UtcNow.AddMinutes(-3), activationRequestedAt: DateTime.UtcNow.AddSeconds(-1));
        var run = await SeedQueuedRunAsync(runtimeId);

        await CreateProcessor().ActivateWaitingTargetsAsync(CancellationToken.None);

        var ended = await ReloadAsync(run.Id);
        ended.Status.ShouldBe(CliRunStatus.Failed);
        ended.FailureReason.ShouldBe(CliRunFailureReason.TargetUnavailable);
        ended.Error.ShouldNotBeNull().ShouldContain(Alice);
        _activator.VerifyNoOtherCalls();
        (await DbContext.Set<CliRuntime>().AsNoTracking().SingleAsync(r => r.Id == runtimeId)).ActivationStartedAt
            .ShouldBeNull("the next run starts a fresh wait");
    }

    [Fact]
    public async Task ARefusedActivation_EndsTheQueuedRunsAtOnce_WithTheReason()
    {
        var runtimeId = await SeedRemoteRuntimeAsync(Alice, online: false, version: null);
        var run = await SeedQueuedRunAsync(runtimeId);
        _activator.Setup(a => a.ActivateAsync(It.IsAny<CliActivationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CliActivationResult.Refused("This user has no workspace."));

        await CreateProcessor().ActivateWaitingTargetsAsync(CancellationToken.None);

        var ended = await ReloadAsync(run.Id);
        ended.Status.ShouldBe(CliRunStatus.Failed);
        ended.FailureReason.ShouldBe(CliRunFailureReason.TargetUnavailable);
        ended.Error.ShouldBe("This user has no workspace.");
    }

    [Fact]
    public async Task AnOnlineWorkspace_IsNotActivated()
    {
        var runtimeId = await SeedRemoteRuntimeAsync(Alice, online: true);
        await SeedQueuedRunAsync(runtimeId);

        await CreateProcessor().ActivateWaitingTargetsAsync(CancellationToken.None);

        _activator.VerifyNoOtherCalls();
    }

    // ── 按运行的工作目录 ──────────────────────────────────────────────────────

    [Fact]
    public async Task TheSameThreadsNextTurn_LandsInTheSameDirectory_AndResumesItsSession_WithNothingWrittenIntoIt()
    {
        await SeedBindingAsync();
        var runtimeId = await SeedRemoteRuntimeAsync(Alice, online: true);
        var workDirectory = $"{ChatRoot}/{ThreadId:N}";
        _host = RemoteHost(Alice);
        await _host.FileSystem.CreateDirectoryAsync(workDirectory, CancellationToken.None);
        await _host.FileSystem.WriteAllTextAsync($"{workDirectory}/notes.md", "the user's own file", CancellationToken.None);

        IReadOnlyList<string>? filesAtStart = null;
        _host.StartWith = spec =>
        {
            filesAtStart ??= ((InMemoryHostFileSystem)_host.FileSystem).Files;
            return SessionProcess("sess-1");
        };

        var first = await SeedClaimedRunAsync(runtimeId, workDirectory);
        await CreateExecutor().ExecuteAsync(first.Id, CancellationToken.None).WaitAsync(TestTimeout);
        (await ReloadAsync(first.Id)).Status.ShouldBe(CliRunStatus.Completed);

        // 占用标记按目录记在工作区根下，不在用户的目录里；运行结束就删掉
        filesAtStart.ShouldNotBeNull().ShouldContain(f => f.StartsWith($"{RemoteRoot}/.tnzi-claims/", StringComparison.Ordinal));
        FilesUnder(workDirectory).ShouldBe([$"{workDirectory}/notes.md"]);
        ((InMemoryHostFileSystem)_host.FileSystem).Files.ShouldNotContain(f => f.StartsWith($"{RemoteRoot}/.tnzi-claims/", StringComparison.Ordinal));

        var second = await SeedClaimedRunAsync(runtimeId, workDirectory);
        await CreateExecutor().ExecuteAsync(second.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var resumed = await ReloadAsync(second.Id);
        resumed.Status.ShouldBe(CliRunStatus.Completed, resumed.Error);
        resumed.WorkDirectory.ShouldBe(workDirectory);
        var sessions = _host.Starts.Where(s => s.Purpose == CliProcessPurpose.AgentSession).ToList();
        sessions.Count.ShouldBe(2);
        sessions[1].WorkingDirectory.ShouldBe(workDirectory);
        sessions[1].Arguments.ShouldContain("--resume");
        sessions[1].Arguments.ShouldContain("sess-1");
        FilesUnder(workDirectory).ShouldBe([$"{workDirectory}/notes.md"]);
        (await _host.FileSystem.ReadAllTextAsync($"{workDirectory}/notes.md", CancellationToken.None)).ShouldBe("the user's own file");
    }

    [Theory]
    [InlineData("/etc/ws")]
    [InlineData(ChatRoot + "/../../../etc")]
    [InlineData(RemoteRoot + "/host/x")]
    [InlineData(RemoteRoot)]
    [InlineData(ChatRoot + "ter")]
    [InlineData("/home/alice")]
    [InlineData("relative/path")]
    public async Task AWorkDirectoryOutsideTheTargetsRoots_FailsTheRun_WithoutStartingAnything(string workDirectory)
    {
        await SeedBindingAsync();
        var runtimeId = await SeedRemoteRuntimeAsync(Alice, online: true);
        _host = RemoteHost(Alice);
        var run = await SeedClaimedRunAsync(runtimeId, workDirectory, create: true);

        await CreateExecutor().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var failed = await ReloadAsync(run.Id);
        failed.Status.ShouldBe(CliRunStatus.Failed);
        failed.FailureReason.ShouldBe(CliRunFailureReason.LaunchFailed);
        _host.Starts.ShouldBeEmpty();
        ((InMemoryHostFileSystem)_host.FileSystem).Files.ShouldBe([RemoteExecutable], "nothing is written for a refused directory");
    }

    [Theory]
    [InlineData(ChatRoot)]
    [InlineData(ChatRoot + "/")]
    [InlineData(ChatRoot + "/x/..")]
    public async Task AWorkDirectoryThatIsOneOfTheTargetsRoots_IsAccepted(string workDirectory)
    {
        await SeedBindingAsync();
        var runtimeId = await SeedRemoteRuntimeAsync(Alice, online: true);
        _host = RemoteHost(Alice);
        await _host.FileSystem.CreateDirectoryAsync(ChatRoot, CancellationToken.None);
        var run = await SeedClaimedRunAsync(runtimeId, workDirectory);

        await CreateExecutor().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var completed = await ReloadAsync(run.Id);
        completed.Status.ShouldBe(CliRunStatus.Completed, completed.Error);
        completed.WorkDirectory.ShouldBe(ChatRoot);
        _host.Starts.Single(s => s.Purpose == CliProcessPurpose.AgentSession).WorkingDirectory.ShouldBe(ChatRoot);
    }

    [Fact]
    public async Task AMissingWorkDirectory_FailsUnlessThePlacementAskedForItToBeCreated()
    {
        await SeedBindingAsync();
        var runtimeId = await SeedRemoteRuntimeAsync(Alice, online: true);
        _host = RemoteHost(Alice);
        var run = await SeedClaimedRunAsync(runtimeId, $"{ChatRoot}/missing", create: false);

        await CreateExecutor().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var failed = await ReloadAsync(run.Id);
        failed.Status.ShouldBe(CliRunStatus.Failed);
        failed.FailureReason.ShouldBe(CliRunFailureReason.WorkspacePrepareFailed);
        (await _host.FileSystem.DirectoryExistsAsync($"{ChatRoot}/missing", CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task AHostThatHandsOutAnotherWorkspace_IsRefused()
    {
        await SeedBindingAsync();
        var runtimeId = await SeedRemoteRuntimeAsync(Alice, online: true);
        _host = RemoteHost(Bob);
        var run = await SeedClaimedRunAsync(runtimeId, workDirectory: null);

        await CreateExecutor().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var failed = await ReloadAsync(run.Id);
        failed.Status.ShouldBe(CliRunStatus.Failed);
        failed.FailureReason.ShouldBe(CliRunFailureReason.LaunchFailed);
        failed.Error.ShouldNotBeNull().ShouldContain(Alice);
        _host.Starts.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithAPerRunWorkDirectory_AReattachedRunRecognisesItsWorkspace_AndRemovesItsManagedFilesAtTheEnd()
    {
        await SeedBindingAsync();
        var runtimeId = await SeedRemoteRuntimeAsync(Alice, online: true);
        var workDirectory = $"{ChatRoot}/{ThreadId:N}";
        var remote = new SimulatedRemoteProcess("proc-ws", line => line.Contains("\"type\":\"user\"", StringComparison.Ordinal)
            ?
            [
                """{"type":"system","subtype":"init","session_id":"sess-w"}""",
                """{"type":"assistant","session_id":"sess-w","message":{"role":"assistant","model":"m","content":[{"type":"tool_use","id":"toolu_1","name":"Bash","input":{"command":"make"}}]}}"""
            ]
            : []);
        _host = RemoteHost(Alice);
        _host.StartWith = _ => remote.Connect();
        _host.Coverage = CliHostCoverage.RemoteOnly([Alice]);
        var run = await SeedClaimedRunAsync(runtimeId, workDirectory, create: true);

        using (var shutdown = new CancellationTokenSource())
        {
            var execution = CreateExecutor().ExecuteAsync(run.Id, shutdown.Token);
            await WaitUntilAsync(async () => await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
                .AnyAsync(m => m.RunId == run.Id && m.Type == CliAgentEventType.ToolUse));
            await shutdown.CancelAsync();
            await execution.WaitAsync(TestTimeout);
        }

        var detached = await ReloadAsync(run.Id);
        detached.Status.ShouldBe(CliRunStatus.Running);
        var mcpConfig = $"{RemoteRoot}/host/{run.Id:N}/{CliWorkspaceLayout.MetadataDirectoryName}/{CliWorkspaceLayout.McpConfigFileName}";
        (await _host.FileSystem.FileExistsAsync(mcpConfig, CancellationToken.None)).ShouldBeTrue("the process still reads its MCP config");

        remote.Emit("""{"type":"result","subtype":"success","session_id":"sess-w","result":"Built.","num_turns":1}""");
        _host.OnReattach = _ => CliReattachResult.Attached(remote.Connect());

        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id);
        await CreateExecutor().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var finished = await ReloadAsync(run.Id);
        finished.Status.ShouldBe(CliRunStatus.Completed, finished.Error);
        finished.Output.ShouldBe("Built.");
        (await _host.FileSystem.FileExistsAsync(mcpConfig, CancellationToken.None)).ShouldBeFalse("the reattached turn cleans up what the first one wrote");
        FilesUnder(workDirectory).ShouldBeEmpty();
        _host.Starts.Count(s => s.Purpose == CliProcessPurpose.AgentSession).ShouldBe(1);
    }

    // ── 凭据来源 ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task TheDeploymentsFallbackToken_GoesToARemoteWorkspaceOnlyWhenAllowed(bool allowed, bool expectToken)
    {
        await SeedBindingAsync();
        var runtimeId = await SeedRemoteRuntimeAsync(Alice, online: true);
        _host = RemoteHost(Alice);
        _host.StartWith = spec => spec.Purpose == CliProcessPurpose.Probe
            ? new ScriptedExitProcess(["""{"loggedIn":false}"""])
            : SessionProcess("sess-t");
        var run = await SeedClaimedRunAsync(runtimeId, workDirectory: null);

        var provider = CliBuiltInProviders.All["claude"] with { AllowFallbackAuthTokenOnRemoteTargets = allowed };
        await CreateExecutor(provider, fallbackToken: "platform-token").ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        var session = _host.Starts.Single(s => s.Purpose == CliProcessPurpose.AgentSession);
        session.Environment.ContainsKey("CLAUDE_CODE_OAUTH_TOKEN").ShouldBe(expectToken);
    }

    [Fact]
    public async Task OnTheLocalHost_TheFallbackTokenIsInjectedAsBefore()
    {
        await SeedBindingAsync();
        _host = RemoteHost(targetId: null);
        _host.StartWith = spec => spec.Purpose == CliProcessPurpose.Probe
            ? new ScriptedExitProcess(["""{"loggedIn":false}"""])
            : SessionProcess("sess-l");
        var run = await SeedClaimedRunAsync(BindingRuntimeId, workDirectory: null);

        await CreateExecutor(fallbackToken: "platform-token").ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        _host.Starts.Single(s => s.Purpose == CliProcessPurpose.AgentSession).Environment["CLAUDE_CODE_OAUTH_TOKEN"].ShouldBe("platform-token");
    }

    // ── 登记 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Registering_UpsertsTheTargetsClis_AndTakesTheOnesItNoLongerReportsOffline()
    {
        var registry = CreateRegistry();
        var first = await registry.RegisterAsync(new CliTargetRegistration
        {
            TargetId = Alice,
            Runtimes =
            [
                new CliTargetRuntime { ProviderKey = "claude", ExecutablePath = RemoteExecutable, Version = "2.1.300" },
                new CliTargetRuntime { ProviderKey = "kimi", ExecutablePath = "/usr/local/bin/kimi", Version = "0.9" },
                new CliTargetRuntime { ProviderKey = "made-up", ExecutablePath = "/bin/x" }
            ]
        });
        first.Runtimes.Select(r => r.ProviderKey).ShouldBe(["claude", "kimi"], ignoreOrder: true);
        first.UnknownProviders.ShouldBe(["made-up"]);
        first.Runtimes.ShouldAllBe(r => r.Mode == CliRuntimeMode.RemoteDaemon && r.Status == CliRuntimeStatus.Online && r.HostId == Alice);

        var second = await registry.RegisterAsync(new CliTargetRegistration
        {
            TargetId = Alice,
            Runtimes = [new CliTargetRuntime { ProviderKey = "claude", ExecutablePath = RemoteExecutable, Version = "2.1.310" }]
        });

        var rows = await DbContext.Set<CliRuntime>().AsNoTracking().Where(r => r.HostId == Alice).ToListAsync();
        rows.Count.ShouldBe(2, "one row per target and CLI; a new version updates it");
        rows.Single(r => r.ProviderKey == "claude").CliVersion.ShouldBe("2.1.310");
        rows.Single(r => r.ProviderKey == "claude").Id.ShouldBe(second.Runtimes.Single().Id);
        rows.Single(r => r.ProviderKey == "kimi").Status.ShouldBe(CliRuntimeStatus.Offline);

        (await registry.MarkOfflineAsync(Alice)).ShouldBe(1);
        (await DbContext.Set<CliRuntime>().AsNoTracking().SingleAsync(r => r.HostId == Alice && r.ProviderKey == "claude"))
            .Status.ShouldBe(CliRuntimeStatus.Offline);
    }

    [Fact]
    public async Task TheServersOwnHostName_CannotBeRegisteredAsARemoteTarget()
    {
        await Should.ThrowAsync<ArgumentException>(() => CreateRegistry().RegisterAsync(new CliTargetRegistration
        {
            TargetId = Environment.MachineName,
            Runtimes = [new CliTargetRuntime { ProviderKey = "claude", ExecutablePath = RemoteExecutable }]
        }));
    }

    [Fact]
    public async Task ACliVersionOutsideTheSupportedRange_IsReportedAtRegistration_AndTheRunIsRefusedWithTheReason()
    {
        await SeedBindingAsync();
        var supported = CliBuiltInProviders.All["claude"] with { MinimumVersion = "2.1.300", MaximumVersion = "2.1" };
        var registration = await CreateRegistry(minimumVersion: "2.1.300", maximumVersion: "2.1").RegisterAsync(new CliTargetRegistration
        {
            TargetId = Alice,
            Runtimes = [new CliTargetRuntime { ProviderKey = "claude", ExecutablePath = RemoteExecutable, Version = "2.0.77 (Claude Code)" }]
        });
        registration.UnsupportedVersions.ShouldContainKey("claude");

        _host = RemoteHost(Alice);
        var run = await SeedClaimedRunAsync(registration.Runtimes.Single().Id, workDirectory: null);
        await CreateExecutor(supported).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var failed = await ReloadAsync(run.Id);
        failed.Status.ShouldBe(CliRunStatus.Failed);
        failed.FailureReason.ShouldBe(CliRunFailureReason.LaunchFailed);
        failed.Error.ShouldNotBeNull().ShouldContain("2.0.77");
        _host.Starts.ShouldBeEmpty();
    }

    // ── 本机宿主不变 ──────────────────────────────────────────────────────────

    [Fact]
    public void TheLocalHost_ServesLocalRuntimesOnly_AndAcceptsPerRunDirectoriesOnlyUnderConfiguredRoots()
    {
        ICliHost local = LocalHosts.Create();
        local.GetCoverage().ShouldBe(CliHostCoverage.InProcess);
        ((ICliHostTarget)local).WorkDirectoryRoots.ShouldBeEmpty();
        ((ICliHostTarget)local).TargetId.ShouldBeNull();

        var configured = Path.Combine(_configuredLocalRoot, "assigned");
        ((ICliHostTarget)LocalHosts.Create(new CliAgentOptions { AssignedWorkDirectoryRoots = [configured] })).WorkDirectoryRoots
            .ShouldBe([configured]);
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    private TestCliHost RemoteHost(string? targetId)
    {
        var host = new TestCliHost(
            (_, _) => Task.FromResult<ICliProcess>(SessionProcess("sess-1")),
            executablePath: RemoteExecutable,
            fileSystem: new InMemoryHostFileSystem(),
            workspacesRoot: RemoteRoot)
        {
            TargetId = targetId,
            WorkDirectoryRoots = [ChatRoot],
            Coverage = targetId is null ? CliHostCoverage.InProcess : CliHostCoverage.InProcessAnd([targetId])
        };

        host.FileSystem.CreateDirectoryAsync("/usr/local/bin", CancellationToken.None).GetAwaiter().GetResult();
        host.FileSystem.WriteAllBytesAsync(RemoteExecutable, [0x7F, 0x45, 0x4C, 0x46], CancellationToken.None).GetAwaiter().GetResult();
        return host;
    }

    private static ICliProcess SessionProcess(string sessionId)
        => new ScriptedExitProcess(
        [
            $$"""{"type":"system","subtype":"init","session_id":"{{sessionId}}"}""",
            $$"""{"type":"result","subtype":"success","session_id":"{{sessionId}}","result":"Done.","num_turns":1}"""
        ]);

    private List<string> FilesUnder(string directory)
        => [.. ((InMemoryHostFileSystem)_host.FileSystem).Files.Where(f => f.StartsWith(directory + "/", StringComparison.Ordinal))];

    private async Task SeedBindingAsync()
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = BindingRuntimeId,
            MaterializeSkills = false,
            InjectAgentInstructions = true,
            WorkDirectoryMode = CliWorkDirectoryMode.PerThread
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = BindingRuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = RemoteExecutable,
            Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<Guid> SeedRemoteRuntimeAsync(
        string targetId, bool online, string? version = "2.1.310", DateTime? activationStartedAt = null, DateTime? activationRequestedAt = null)
    {
        var runtime = new CliRuntime
        {
            HostId = targetId,
            ProviderKey = "claude",
            Name = $"claude @ {targetId}",
            ExecutablePath = version is null ? string.Empty : RemoteExecutable,
            CliVersion = version,
            Mode = CliRuntimeMode.RemoteDaemon,
            Status = online ? CliRuntimeStatus.Online : CliRuntimeStatus.Offline,
            LastSeenAt = online ? DateTime.UtcNow : null,
            ActivationStartedAt = activationStartedAt,
            ActivationRequestedAt = activationRequestedAt
        };
        DbContext.Set<CliRuntime>().Add(runtime);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return runtime.Id;
    }

    private async Task<CliRun> SeedQueuedRunAsync(Guid runtimeId, string? workDirectory = null, bool create = false)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = runtimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Queued,
            Prompt = "do the thing",
            AssignedWorkDirectory = workDirectory,
            CreateAssignedWorkDirectory = create
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task<CliRun> SeedClaimedRunAsync(Guid runtimeId, string? workDirectory, bool create = false)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = runtimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = "do the thing",
            AssignedWorkDirectory = workDirectory,
            CreateAssignedWorkDirectory = create
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task<CliRun> SeedDetachedRunAsync(Guid runtimeId, DateTime detachedAt)
    {
        await SeedBindingAsync();
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = runtimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Running,
            Prompt = "do the thing",
            ReattachHandle = "proc-x",
            DetachedAt = detachedAt,
            StartedAt = detachedAt.AddMinutes(-5),
            DispatchedAt = detachedAt.AddMinutes(-5),
            LeaseExpiresAt = detachedAt,
            WorkDirectory = $"{ChatRoot}/{ThreadId:N}"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task<CliRun> ReloadAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TestTimeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(20);
        }
    }

    private TestOptionsMonitor<CliAgentOptions> Options(string? fallbackToken = null, string? minimumVersion = null, string? maximumVersion = null)
    {
        var options = new CliAgentOptions
        {
            Enabled = true,
            WorkspacesRoot = _configuredLocalRoot,
            LeaseDuration = TimeSpan.FromMinutes(30),
            WriteBack = new CliWriteBackOptions { Enabled = true, McpEndpoint = "https://platform.example.com/mcp", AllowedTools = ["*"] }
        };
        options.Providers["claude"] = new CliProviderOptions
        {
            FallbackAuthToken = fallbackToken,
            MinimumVersion = minimumVersion,
            MaximumVersion = maximumVersion
        };
        return new TestOptionsMonitor<CliAgentOptions>(options);
    }

    private CliRunQueueProcessor CreateProcessor()
        => new(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            Options(),
            NullLogger<CliRunQueueProcessor>.Instance);

    private CliRemoteRuntimeRegistry CreateRegistry(string? minimumVersion = null, string? maximumVersion = null)
    {
        var options = Options(minimumVersion: minimumVersion, maximumVersion: maximumVersion);
        return new CliRemoteRuntimeRegistry(
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            new CliProviderRegistry(options),
            options,
            new CliRunQueueSignal(),
            NullLogger<CliRemoteRuntimeRegistry>.Instance);
    }

    private CliRunExecutor CreateExecutor(CliProviderDescriptor? provider = null, string? fallbackToken = null)
    {
        var runs = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find("claude")).Returns(provider ?? CliBuiltInProviders.All["claude"]);

        var agents = new Mock<IRepository<Agent, Guid>>();
        agents.Setup(r => r.GetAsync(AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = AgentId, Name = "workspace-agent", Instructions = "Answer briefly." });

        var options = Options(fallbackToken);
        return new CliRunExecutor(
            runs,
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            agents.Object,
            registry.Object,
            new CliProtocolAdapterFactory(ServiceProvider),
            _host,
            new FileSystemWorkspacePreparer(options, NullLogger<FileSystemWorkspacePreparer>.Instance),
            new CliBriefComposer(),
            new CliMcpConfigComposer(NullLogger<CliMcpConfigComposer>.Instance),
            new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
            new CliLaunchEnvironmentComposer(new CliAuthStatusProbe(NullLogger<CliAuthStatusProbe>.Instance), NullLogger<CliLaunchEnvironmentComposer>.Instance),
            Mock.Of<IAgentGrantService>(),
            Mock.Of<ISkillService>(),
            new CliRunSignalHub(),
            options,
            ServiceProvider.GetRequiredService<ICurrentTenant>(),
            NullLogger<CliRunExecutor>.Instance,
            ServiceProvider.GetRequiredService<IServiceScopeFactory>());
    }
}
