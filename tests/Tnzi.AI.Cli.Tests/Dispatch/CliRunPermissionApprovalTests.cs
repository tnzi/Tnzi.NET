namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 「先问我」档在有人值守的一轮上：CLI 的权限请求落库、作为事件到达使用方，回答（任何一个服务端副本收下）写给 CLI，
/// 超时与取消按拒绝结束；服务端重启后重新附着的运行上，挂着的请求照常等回答，已经写出的回答在重放里对得上序号与 stdin 行数。
/// </summary>
/// <remarks>
/// 真实的执行器、调度器、队列处理器与 stream-json 适配器，SQLite 仓储；执行宿主是 <see cref="SimulatedRemoteProcess"/>。
/// 执行器与调度器各用自己的 <see cref="CliRunSignalHub"/>：回答从「另一个副本」到达，只能靠轮询读到。
/// </remarks>
public class CliRunPermissionApprovalTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111e1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222e2");
    private static readonly Guid ThreadId = Guid.Parse("33333333-3333-3333-3333-3333333333e3");
    private static readonly Guid SomeoneElse = Guid.Parse("99999999-9999-9999-9999-9999999999e9");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private const string RemoteRoot = "/home/agent/workspaces";
    private const string RemoteExecutable = "/usr/local/bin/agent-cli";

    private const string Init = """{"type":"system","subtype":"init","session_id":"sess-p"}""";
    private const string ToolUse = """{"type":"assistant","session_id":"sess-p","message":{"role":"assistant","model":"m","content":[{"type":"tool_use","id":"toolu_1","name":"Bash","input":{"command":"rm -rf build"}}]}}""";
    private const string CanUseTool = """{"type":"control_request","request_id":"req-1","request":{"subtype":"can_use_tool","tool_name":"Bash","tool_use_id":"toolu_1","input":{"command":"rm -rf build"},"description":"Remove the build directory"}}""";
    private const string ToolResult = """{"type":"user","session_id":"sess-p","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"done"}]}}""";
    private const string Finished = """{"type":"result","subtype":"success","session_id":"sess-p","result":"Finished.","num_turns":2}""";

    private readonly string _configuredLocalRoot = TestScratch.NewPath("approval-cfg");
    private Mock<IPermissionChecker>? _permissionChecker;

    public CliRunPermissionApprovalTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliBudgetDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliRunPermissionRequest, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunPermissionRequest, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliBudgetDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliBudgetDbContext, CliRuntime, Guid>>();

        _permissionChecker = new Mock<IPermissionChecker>();
        _permissionChecker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);
        services.AddScoped(_ => _permissionChecker.Object);
    }

    public override void Dispose()
    {
        TestScratch.Delete(_configuredLocalRoot);
        base.Dispose();
    }

    // ── 回答、超时、取消 ─────────────────────────────────────────────────────

    [Fact]
    public async Task TheRequestReachesTheUser_AndTheAnswerFromAnotherReplicaIsWrittenToTheCli()
    {
        var remote = Remote(handle: null, onAnswer: _ => [ToolResult, Finished]);
        var host = await CreateHostAsync(remote);
        var run = await SeedClaimedRunAsync(attended: true);

        var execution = CreateExecutor(host).ExecuteAsync(run.Id, CancellationToken.None);
        var request = await WaitForRequestAsync(run.Id);

        request.Tool.ShouldBe("Bash");
        request.CallId.ShouldBe("toolu_1");
        request.Content.ShouldBe("Remove the build directory");
        var permission = CliRunMessageConverter.DeserializePermission(request.PermissionJson)!;
        permission.RequestId.ShouldNotBeNull();
        permission.Status.ShouldBe(CliPermissionStatus.Pending);
        permission.ExpiresAt!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
        permission.Options.Select(o => o.Id).ShouldBe([CliPermissionOptionKinds.AllowOnce, CliPermissionOptionKinds.RejectOnce]);
        remote.Stdin.ShouldNotContain(l => l.Contains("control_response"), "nothing is answered before the user answers");

        var answered = await CreateDispatcher().AnswerPermissionAsync(
            run.Id, permission.RequestId!.Value, new CliPermissionAnswerDto { OptionId = CliPermissionOptionKinds.AllowOnce });
        answered.Succeeded.ShouldBeTrue(answered.Message);

        await execution.WaitAsync(TestTimeout);

        var finished = await ReloadAsync(run.Id);
        finished.Status.ShouldBe(CliRunStatus.Completed, finished.Error);
        remote.Stdin.Single(l => l.Contains("control_response")).ShouldContain("\"behavior\":\"allow\"");

        var row = await RequestRowAsync(permission.RequestId.Value);
        row.Status.ShouldBe(CliPermissionStatus.Allowed);
        row.AnsweredById.ShouldBe(TestHelper.DefaultTestUserId);

        // 结局事件排在请求之后、CLI 因回答而写出的工具结果之前；序号连续
        var messages = await MessagesAsync(run.Id);
        messages.Select(m => m.Sequence).ShouldBe(Enumerable.Range(1, messages.Count));
        var resolved = messages.Single(m => m.Type == CliAgentEventType.PermissionResolved);
        row.ResolvedSequence.ShouldBe(resolved.Sequence);
        resolved.Sequence.ShouldBeGreaterThan(request.Sequence);
        resolved.Sequence.ShouldBeLessThan(messages.Single(m => m.Type == CliAgentEventType.ToolResult).Sequence);
        var outcome = CliRunMessageConverter.DeserializePermission(resolved.PermissionJson)!;
        outcome.RequestId.ShouldBe(permission.RequestId);
        outcome.Status.ShouldBe(CliPermissionStatus.Allowed);
        outcome.OptionId.ShouldBe(CliPermissionOptionKinds.AllowOnce);
        outcome.AnsweredBy.ShouldBe(TestHelper.DefaultTestUserId);
        resolved.CallId.ShouldBe("toolu_1");
    }

    [Fact]
    public async Task ARequestNobodyAnswers_IsDeniedWhenItTimesOut()
    {
        var remote = Remote(handle: null, onAnswer: _ => [ToolResult, Finished]);
        var host = await CreateHostAsync(remote);
        var run = await SeedClaimedRunAsync(attended: true);

        await CreateExecutor(host, timeout: TimeSpan.FromMilliseconds(300)).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        remote.Stdin.Single(l => l.Contains("control_response")).ShouldContain("\"behavior\":\"deny\"");

        var messages = await MessagesAsync(run.Id);
        var outcome = CliRunMessageConverter.DeserializePermission(
            messages.Single(m => m.Type == CliAgentEventType.PermissionResolved).PermissionJson)!;
        outcome.Status.ShouldBe(CliPermissionStatus.TimedOut);
        (await RequestRowAsync(outcome.RequestId!.Value)).Status.ShouldBe(CliPermissionStatus.TimedOut);

        // 超时之后再答：请求已经有了结局
        var late = await CreateDispatcher().AnswerPermissionAsync(
            run.Id, outcome.RequestId.Value, new CliPermissionAnswerDto { OptionId = CliPermissionOptionKinds.AllowOnce });
        late.Code.ShouldBe(409);
        late.ErrorCode.ShouldBe(ErrorCodes.CliPermissionRequestClosed);
    }

    [Fact]
    public async Task CancellingTheRun_EndsTheWaitingRequestAsDenied()
    {
        var remote = Remote(handle: null, onAnswer: _ => [ToolResult, Finished]);
        var host = await CreateHostAsync(remote);
        var run = await SeedClaimedRunAsync(attended: true);

        using var cancel = new CancellationTokenSource();
        var execution = CreateExecutor(host).ExecuteAsync(run.Id, cancel.Token);
        var permission = CliRunMessageConverter.DeserializePermission((await WaitForRequestAsync(run.Id)).PermissionJson)!;

        await DbContext.Set<CliRun>().IgnoreQueryFilters().Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true));
        await cancel.CancelAsync();
        await execution.WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Cancelled);
        (await RequestRowAsync(permission.RequestId!.Value)).Status.ShouldBe(CliPermissionStatus.Cancelled);
        var outcome = CliRunMessageConverter.DeserializePermission(
            (await MessagesAsync(run.Id)).Single(m => m.Type == CliAgentEventType.PermissionResolved).PermissionJson)!;
        outcome.Status.ShouldBe(CliPermissionStatus.Cancelled);

        var late = await CreateDispatcher().AnswerPermissionAsync(
            run.Id, permission.RequestId.Value, new CliPermissionAnswerDto { OptionId = CliPermissionOptionKinds.AllowOnce });
        late.Code.ShouldBe(409);
    }

    /// <summary>没有人值守的一轮（定时任务）不发权限请求：按档位当场拒绝，不建请求行，也不等。</summary>
    [Fact]
    public async Task AnUnattendedTurn_IsDeniedOnTheSpot_WithoutARequest()
    {
        var remote = Remote(handle: null, onAnswer: _ => [ToolResult, Finished]);
        var host = await CreateHostAsync(remote);
        var run = await SeedClaimedRunAsync(attended: false);

        await CreateExecutor(host).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        remote.Stdin.Single(l => l.Contains("control_response")).ShouldContain("\"behavior\":\"deny\"");
        (await MessagesAsync(run.Id)).ShouldNotContain(m => m.Type == CliAgentEventType.PermissionRequest);
        (await DbContext.Set<CliRunPermissionRequest>().IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    /// <summary>启动档案能改写调用方给的值：调用方说没人值守，消费方按这一轮的来源知道有人在等。</summary>
    [Fact]
    public async Task TheLaunchProfileCanMarkATurnAttended()
    {
        var remote = Remote(handle: null, onAnswer: _ => [ToolResult, Finished]);
        var host = await CreateHostAsync(remote);
        host.LaunchProfile = _ => new CliLaunchProfile { PermissionMode = CliPermissionMode.Ask, Attended = true };
        var run = await SeedClaimedRunAsync(attended: false);

        var execution = CreateExecutor(host).ExecuteAsync(run.Id, CancellationToken.None);
        var permission = CliRunMessageConverter.DeserializePermission((await WaitForRequestAsync(run.Id)).PermissionJson)!;
        (await CreateDispatcher().AnswerPermissionAsync(run.Id, permission.RequestId!.Value,
            new CliPermissionAnswerDto { OptionId = CliPermissionOptionKinds.RejectOnce, Message = "Keep it." })).Succeeded.ShouldBeTrue();
        await execution.WaitAsync(TestTimeout);

        var answer = remote.Stdin.Single(l => l.Contains("control_response"));
        answer.ShouldContain("\"behavior\":\"deny\"");
        answer.ShouldContain("Keep it.");
        (await RequestRowAsync(permission.RequestId.Value)).Status.ShouldBe(CliPermissionStatus.Denied);
    }

    // ── 重新附着 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 停机时还在等的请求不丢：服务端不在的时候用户答了，接回来之后重放追上已落库的事件，再把回答写给 CLI（只写一次）。
    /// </summary>
    [Fact]
    public async Task APendingRequestSurvivesARestart_AndAnAnswerGivenWhileTheServerWasDownIsWrittenAfterReattaching()
    {
        var remote = Remote(handle: "proc-p", onAnswer: _ => [ToolResult, Finished]);
        var host = await CreateHostAsync(remote);
        var run = await SeedClaimedRunAsync(attended: true);

        Guid requestId;
        using (var shutdown = new CancellationTokenSource())
        {
            var execution = CreateExecutor(host).ExecuteAsync(run.Id, shutdown.Token);
            requestId = CliRunMessageConverter.DeserializePermission((await WaitForRequestAsync(run.Id)).PermissionJson)!.RequestId!.Value;
            await shutdown.CancelAsync();
            await execution.WaitAsync(TestTimeout);
        }

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Running);
        (await RequestRowAsync(requestId)).Status.ShouldBe(CliPermissionStatus.Pending, "a detached run keeps its requests waiting");
        var before = await MessagesAsync(run.Id);

        // 服务端不在：回答照样收下（运行仍是 Running）
        (await CreateDispatcher().AnswerPermissionAsync(run.Id, requestId,
            new CliPermissionAnswerDto { OptionId = CliPermissionOptionKinds.AllowOnce })).Succeeded.ShouldBeTrue();

        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id);
        host.OnReattach = _ => CliReattachResult.Attached(remote.Connect());
        await CreateExecutor(host).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var finished = await ReloadAsync(run.Id);
        finished.Status.ShouldBe(CliRunStatus.Completed, finished.Error);
        remote.Stdin.Count(l => l.Contains("control_response")).ShouldBe(1);
        remote.Stdin.Single(l => l.Contains("control_response")).ShouldContain("\"behavior\":\"allow\"");

        var after = await MessagesAsync(run.Id);
        after.Take(before.Count).Select(m => m.Id).ShouldBe(before.Select(m => m.Id));
        after.Select(m => m.Sequence).ShouldBe(Enumerable.Range(1, after.Count));
        after.Count(m => m.Type == CliAgentEventType.PermissionRequest).ShouldBe(1);
        after.Count(m => m.Type == CliAgentEventType.PermissionResolved).ShouldBe(1);
        (await DbContext.Set<CliRunPermissionRequest>().IgnoreQueryFilters().CountAsync()).ShouldBe(1);
    }

    /// <summary>
    /// 停机前已经写给 CLI 的回答：重放到它的序号时原样交回适配器（那一行 stdin 不再发），之后的事件不重不漏。
    /// </summary>
    [Fact]
    public async Task AnAnswerWrittenBeforeTheRestart_IsReplayedInPlace_WithoutResendingItOrShiftingTheEvents()
    {
        var remote = Remote(handle: "proc-q", onAnswer: _ => [ToolResult]);
        var host = await CreateHostAsync(remote);
        var run = await SeedClaimedRunAsync(attended: true);

        using (var shutdown = new CancellationTokenSource())
        {
            var execution = CreateExecutor(host).ExecuteAsync(run.Id, shutdown.Token);
            var requestId = CliRunMessageConverter.DeserializePermission((await WaitForRequestAsync(run.Id)).PermissionJson)!.RequestId!.Value;
            (await CreateDispatcher().AnswerPermissionAsync(run.Id, requestId,
                new CliPermissionAnswerDto { OptionId = CliPermissionOptionKinds.AllowOnce })).Succeeded.ShouldBeTrue();
            await WaitUntilAsync(async () => (await MessagesAsync(run.Id)).Any(m => m.Type == CliAgentEventType.ToolResult));
            await shutdown.CancelAsync();
            await execution.WaitAsync(TestTimeout);
        }

        var before = await MessagesAsync(run.Id);
        var delivered = remote.Stdin.Count;

        // 服务端不在的时候 CLI 跑完了
        remote.Emit(Finished);

        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id);
        host.OnReattach = _ => CliReattachResult.Attached(remote.Connect());
        await CreateExecutor(host).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var finished = await ReloadAsync(run.Id);
        finished.Status.ShouldBe(CliRunStatus.Completed, finished.Error);
        finished.Output.ShouldBe("Finished.");
        remote.Stdin.Count.ShouldBe(delivered, "the answer the process already had is not sent again");

        var after = await MessagesAsync(run.Id);
        after.Select(m => m.Id).ShouldBe(before.Select(m => m.Id), "every event had been written before the restart");
        after.Count(m => m.Type == CliAgentEventType.PermissionResolved).ShouldBe(1);
        after.Count(m => m.Type == CliAgentEventType.ToolResult).ShouldBe(1);
        finished.MessageCount.ShouldBe(after.Count);
    }

    // ── 回答接口的权限 ───────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyTheUserWhoStartedTheRun_OrAHolderOfTheExecuteCode_CanAnswer()
    {
        var (runId, requestId) = await SeedWaitingRequestAsync(SomeoneElse);
        var answer = new CliPermissionAnswerDto { OptionId = CliPermissionOptionKinds.AllowOnce };

        var stranger = await CreateDispatcher().AnswerPermissionAsync(runId, requestId, answer);
        stranger.Code.ShouldBe(404);
        (await RequestRowAsync(requestId)).Status.ShouldBe(CliPermissionStatus.Pending);

        // 只能看（ai.cliRun.view）不够
        _permissionChecker!.Setup(p => p.IsGrantedAsync(CliPermissions.CliRunView)).ReturnsAsync(true);
        (await CreateDispatcher().AnswerPermissionAsync(runId, requestId, answer)).Code.ShouldBe(404);

        _permissionChecker.Setup(p => p.IsGrantedAsync(CliPermissions.CliRunExecute)).ReturnsAsync(true);
        (await CreateDispatcher().AnswerPermissionAsync(runId, requestId, answer)).Succeeded.ShouldBeTrue();
        (await RequestRowAsync(requestId)).Status.ShouldBe(CliPermissionStatus.Allowed);
    }

    [Fact]
    public async Task AnAnswerMustBeOneOfTheOfferedOptions_AndCountsOnlyOnce()
    {
        var (runId, requestId) = await SeedWaitingRequestAsync(TestHelper.DefaultTestUserId);
        var dispatcher = CreateDispatcher();

        var invalid = await dispatcher.AnswerPermissionAsync(runId, requestId, new CliPermissionAnswerDto { OptionId = "allow_everything" });
        invalid.Code.ShouldBe(400);
        invalid.ErrorCode.ShouldBe(ErrorCodes.CliPermissionOptionInvalid);

        var unknown = await dispatcher.AnswerPermissionAsync(runId, Guid.NewGuid(), new CliPermissionAnswerDto { OptionId = "allow_once" });
        unknown.Code.ShouldBe(404);
        unknown.ErrorCode.ShouldBe(ErrorCodes.CliPermissionRequestNotFound);

        var deny = await dispatcher.AnswerPermissionAsync(runId, requestId,
            new CliPermissionAnswerDto { OptionId = CliPermissionOptionKinds.RejectOnce, Message = "  Not now.  " });
        deny.Succeeded.ShouldBeTrue(deny.Message);
        var row = await RequestRowAsync(requestId);
        row.Status.ShouldBe(CliPermissionStatus.Denied);
        row.Message.ShouldBe("Not now.");

        var again = await dispatcher.AnswerPermissionAsync(runId, requestId, new CliPermissionAnswerDto { OptionId = CliPermissionOptionKinds.AllowOnce });
        again.Code.ShouldBe(409);
        again.ErrorCode.ShouldBe(ErrorCodes.CliPermissionRequestClosed);
        (await RequestRowAsync(requestId)).Status.ShouldBe(CliPermissionStatus.Denied);
    }

    /// <summary>运行进入终态（含宿主死掉之后的租约回收），还没写给 CLI 的请求随之结束，不会永远挂在「等回答」上。</summary>
    [Fact]
    public async Task FinishingTheRun_ClosesItsOpenRequests()
    {
        var (runId, requestId) = await SeedWaitingRequestAsync(TestHelper.DefaultTestUserId);

        var finalizer = new CliRunFinalizer(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(), null, NullLogger.Instance,
            ServiceProvider.GetRequiredService<IRepository<CliRunPermissionRequest, Guid>>());
        (await finalizer.FinishAsync(runId, new CliRunTermination(CliRunStatus.Failed, CliRunFailureReason.Unknown, "Host died."))).ShouldBeTrue();

        (await RequestRowAsync(requestId)).Status.ShouldBe(CliPermissionStatus.Cancelled);
        (await CreateDispatcher().AnswerPermissionAsync(runId, requestId,
            new CliPermissionAnswerDto { OptionId = CliPermissionOptionKinds.AllowOnce })).Code.ShouldBe(409);
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    /// <summary>一个 claude 进程：收到提示后调一个工具并请求批准，收到应答后按 <paramref name="onAnswer"/> 接着写。</summary>
    private static SimulatedRemoteProcess Remote(string? handle, Func<string, IEnumerable<string>> onAnswer)
        => new(handle, line =>
            line.Contains("\"type\":\"user\"", StringComparison.Ordinal) ? [Init, ToolUse, CanUseTool]
            : line.Contains("control_response", StringComparison.Ordinal) ? onAnswer(line)
            : []);

    private async Task<TestCliHost> CreateHostAsync(SimulatedRemoteProcess remote)
    {
        var host = new TestCliHost(
            (_, _) => Task.FromResult<ICliProcess>(remote.Connect()),
            executablePath: RemoteExecutable,
            fileSystem: new InMemoryHostFileSystem(),
            workspacesRoot: RemoteRoot)
        {
            LaunchProfile = _ => new CliLaunchProfile { PermissionMode = CliPermissionMode.Ask }
        };

        var paths = host.FileSystem.Paths;
        await host.FileSystem.CreateDirectoryAsync(paths.GetDirectoryName(RemoteExecutable)!, CancellationToken.None);
        await host.FileSystem.WriteAllBytesAsync(RemoteExecutable, [0x7F, 0x45, 0x4C, 0x46], CancellationToken.None);
        return host;
    }

    private async Task SeedSetupAsync()
    {
        if (await DbContext.Set<CliAgentBinding>().AnyAsync())
        {
            return;
        }

        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            MaterializeSkills = false,
            InjectAgentInstructions = true,
            WorkDirectoryMode = CliWorkDirectoryMode.PerThread
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = RemoteExecutable,
            Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<CliRun> SeedClaimedRunAsync(bool attended)
    {
        await SeedSetupAsync();
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = "clean up",
            Attended = attended
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    /// <summary>一条正在跑的运行与它一个等回答的请求（不起执行器）。</summary>
    private async Task<(Guid RunId, Guid RequestId)> SeedWaitingRequestAsync(Guid creatorId)
    {
        await SeedSetupAsync();
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            Status = CliRunStatus.Running,
            Prompt = "clean up",
            Attended = true
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        // 审计戳按当前用户填 CreatorId：别人的运行要显式改回去
        await DbContext.Set<CliRun>().IgnoreQueryFilters().Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatorId, creatorId));

        var request = new CliRunPermissionRequest
        {
            RunId = run.Id,
            Sequence = 3,
            ProtocolKey = "req-1",
            Tool = "Bash",
            OptionsJson = JsonSerializer.Serialize(
                new[]
                {
                    new CliPermissionOption(CliPermissionOptionKinds.AllowOnce, "Allow once", CliPermissionOptionKinds.AllowOnce),
                    new CliPermissionOption(CliPermissionOptionKinds.RejectOnce, "Deny", CliPermissionOptionKinds.RejectOnce)
                },
                TnziJsonDefaults.Options),
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        DbContext.Set<CliRunPermissionRequest>().Add(request);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return (run.Id, request.Id);
    }

    private async Task<CliRunMessage> WaitForRequestAsync(Guid runId)
    {
        CliRunMessage? request = null;
        await WaitUntilAsync(async () =>
        {
            request = (await MessagesAsync(runId)).FirstOrDefault(m => m.Type == CliAgentEventType.PermissionRequest);
            return request is not null;
        });
        return request!;
    }

    private async Task<List<CliRunMessage>> MessagesAsync(Guid runId)
        => await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.RunId == runId).OrderBy(m => m.Sequence).ToListAsync();

    private async Task<CliRunPermissionRequest> RequestRowAsync(Guid requestId)
        => await DbContext.Set<CliRunPermissionRequest>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == requestId);

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

    private TestOptionsMonitor<CliAgentOptions> Options(TimeSpan? timeout = null) => new(new CliAgentOptions
    {
        Enabled = true,
        WorkspacesRoot = _configuredLocalRoot,
        LeaseDuration = TimeSpan.FromMinutes(30),
        Approval = new CliApprovalOptions { Timeout = timeout ?? TimeSpan.FromMinutes(5), PollInterval = TimeSpan.FromMilliseconds(50) }
    });

    private CliRunQueueProcessor CreateProcessor()
        => new(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            Options(),
            NullLogger<CliRunQueueProcessor>.Instance);

    private CliAgentDispatcher CreateDispatcher()
        => new(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            Mock.Of<IRepository<AgentThread, Guid>>(),
            TestAgents.Repository(AgentId),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            Options(),
            ServiceProvider,
            permissionRepository: ServiceProvider.GetRequiredService<IRepository<CliRunPermissionRequest, Guid>>());

    private CliRunExecutor CreateExecutor(ICliHost host, TimeSpan? timeout = null)
    {
        var runs = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find("claude")).Returns(CliBuiltInProviders.All["claude"]);

        var agents = new Mock<IRepository<Agent, Guid>>();
        agents.Setup(r => r.GetAsync(AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = AgentId, Name = "remote-agent", Instructions = "Answer briefly." });

        var options = Options(timeout);
        return new CliRunExecutor(
            runs,
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            agents.Object,
            registry.Object,
            new CliProtocolAdapterFactory(ServiceProvider),
            host,
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
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            permissionRepository: ServiceProvider.GetRequiredService<IRepository<CliRunPermissionRequest, Guid>>());
    }
}
