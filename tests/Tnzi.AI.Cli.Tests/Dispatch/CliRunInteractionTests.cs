namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 有人值守的一轮上 agent 问用户问题、交出计划等批准：请求落库、作为事件到达使用方，回答（任何一个服务端副本收下）按问题校验后写给 CLI，
/// 结局事件带上回答；服务端重启后在重放里对得上序号、只写一次。没有人值守时 CLI 拿不到提问工具，也不建请求行。
/// </summary>
/// <remarks>
/// 真实的执行器、调度器、队列处理器与 stream-json 适配器，SQLite 仓储；CLI 那一侧回放 <c>Adapters/Recordings/</c> 里录下的真实交换
/// （claude 2.1.316），执行宿主是 <see cref="SimulatedRemoteProcess"/>。执行器与调度器各用自己的 <see cref="CliRunSignalHub"/>：
/// 回答从「另一个副本」到达，只能靠轮询读到。
/// </remarks>
public class CliRunInteractionTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111f1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222f2");
    private static readonly Guid ThreadId = Guid.Parse("33333333-3333-3333-3333-3333333333f3");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private const string ColorQuestion = "Which color do you prefer?";
    private const string ExtrasQuestion = "Which extras do you want? (select any)";
    private const string RemoteRoot = "/home/agent/workspaces";
    private const string RemoteExecutable = "/usr/local/bin/agent-cli";

    private readonly string _configuredLocalRoot = TestScratch.NewPath("interaction-cfg");
    private readonly string _databaseDirectory = TestScratch.CreateDirectory("interaction-db");
    private readonly List<CliProcessSpec> _launched = [];

    public CliRunInteractionTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        // 这一组用例的回放帧多：执行器的主循环、批准通道的轮询与测试线程同时读写库。基类那一条共享的内存连接经不起并发
        // （实测 SQLite Error 5: database is locked，执行器随之失败），换成文件库、每个作用域各自一条连接，写冲突按 SQLite 的忙等待排队
        services.RemoveAll<DbContextOptions<CliBudgetDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<CliBudgetDbContext>>();
        services.AddDbContext<CliBudgetDbContext>(options => options
            .UseSqlite($"Data Source={Path.Combine(_databaseDirectory, "runs.sqlite")};Pooling=False")
            .EnableSensitiveDataLogging());

        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliBudgetDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliRunPermissionRequest, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunPermissionRequest, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliBudgetDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliBudgetDbContext, CliRuntime, Guid>>();

        var permissionChecker = new Mock<IPermissionChecker>();
        permissionChecker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);
        services.AddScoped(_ => permissionChecker.Object);
    }

    public override void Dispose()
    {
        TestScratch.Delete(_configuredLocalRoot);
        base.Dispose();
        TestScratch.Delete(_databaseDirectory);
    }

    // ── 问题 ─────────────────────────────────────────────────────────────────

    /// <summary>全权档的一轮（有人值守）：问题到达使用方，回答按问题存下、写给 CLI，结局事件带回答，上下文占用记在运行上。</summary>
    [Fact]
    public async Task AQuestionInAFullRun_ReachesTheUser_AndTheAnswersAreWrittenToTheCli()
    {
        var remote = Remote("claude-ask-user-question.jsonl");
        var host = await CreateHostAsync(remote, CliPermissionMode.Full);
        var run = await SeedClaimedRunAsync(attended: true);

        var execution = CreateExecutor(host).ExecuteAsync(run.Id, CancellationToken.None);
        var request = await WaitForRequestAsync(run.Id);

        var permission = CliRunMessageConverter.DeserializePermission(request.PermissionJson)!;
        permission.Kind.ShouldBe(CliInteractionKinds.Question);
        permission.Questions!.Select(q => q.Question).ShouldBe([ColorQuestion, ExtrasQuestion]);
        permission.ExpiresAt.ShouldNotBeNull();
        var row = await RequestRowAsync(permission.RequestId!.Value);
        row.Kind.ShouldBe(CliInteractionKinds.Question);
        row.QuestionsJson.ShouldNotBeNull();
        remote.Stdin.ShouldNotContain(l => l.Contains("control_response"), "nothing is answered before the user answers");

        var answered = await CreateDispatcher().AnswerPermissionAsync(run.Id, permission.RequestId.Value, new CliPermissionAnswerDto
        {
            OptionId = CliInteractionOptionIds.Submit,
            Answers = new() { [ColorQuestion] = ["  Red "], [ExtrasQuestion] = ["Stripes", "Dots"] }
        });
        answered.Succeeded.ShouldBeTrue(answered.Message);

        await execution.WaitAsync(TestTimeout);

        var finished = await ReloadAsync(run.Id);
        finished.Status.ShouldBe(CliRunStatus.Completed, finished.Error);
        finished.ContextTokens.ShouldBe(2 + 69072 + 2051 + 37);
        finished.ContextWindow.ShouldBe(1_000_000);

        _launched.Single().Arguments.ShouldContain("--permission-prompt-tool");
        var written = remote.Stdin.Single(l => l.Contains("control_response"));
        written.ShouldContain("\"behavior\":\"allow\"");
        using (var reply = JsonDocument.Parse(written))
        {
            var answers = reply.RootElement.GetProperty("response").GetProperty("response").GetProperty("updatedInput").GetProperty("answers");
            answers.GetProperty(ColorQuestion).GetString().ShouldBe("Red");
            answers.GetProperty(ExtrasQuestion).GetString().ShouldBe("Stripes, Dots");
        }

        var resolved = (await MessagesAsync(run.Id)).Single(m => m.Type == CliAgentEventType.PermissionResolved);
        var outcome = CliRunMessageConverter.DeserializePermission(resolved.PermissionJson)!;
        outcome.Kind.ShouldBe(CliInteractionKinds.Question);
        outcome.Status.ShouldBe(CliPermissionStatus.Allowed);
        outcome.Answers![ExtrasQuestion].ShouldBe(["Stripes", "Dots"]);
        (await RequestRowAsync(permission.RequestId.Value)).Status.ShouldBe(CliPermissionStatus.Allowed);
    }

    /// <summary>回答要对得上问题：缺了、多了、单选答了两个都 400，请求照旧等着；不答不需要回答。</summary>
    [Fact]
    public async Task Answers_MustFitTheQuestions_AndDecliningNeedsNone()
    {
        var remote = Remote("claude-ask-user-question.jsonl");
        var host = await CreateHostAsync(remote, CliPermissionMode.Full);
        var run = await SeedClaimedRunAsync(attended: true);

        var execution = CreateExecutor(host).ExecuteAsync(run.Id, CancellationToken.None);
        var requestId = CliRunMessageConverter.DeserializePermission((await WaitForRequestAsync(run.Id)).PermissionJson)!.RequestId!.Value;
        var dispatcher = CreateDispatcher();

        foreach (var answers in new Dictionary<string, List<string>>?[]
                 {
                     null,
                     new() { [ColorQuestion] = ["Red"] },
                     new() { [ColorQuestion] = ["Red"], [ExtrasQuestion] = ["Dots"], ["Something else?"] = ["Yes"] },
                     new() { [ColorQuestion] = ["Red", "Blue"], [ExtrasQuestion] = ["Dots"] },
                     new() { [ColorQuestion] = ["  "], [ExtrasQuestion] = ["Dots"] }
                 })
        {
            var invalid = await dispatcher.AnswerPermissionAsync(run.Id, requestId,
                new CliPermissionAnswerDto { OptionId = CliInteractionOptionIds.Submit, Answers = answers });
            invalid.Code.ShouldBe(400);
            invalid.ErrorCode.ShouldBe(ErrorCodes.CliQuestionAnswersInvalid);
        }

        (await RequestRowAsync(requestId)).Status.ShouldBe(CliPermissionStatus.Pending);

        // 不答：不需要 answers，附的话交给 agent
        (await dispatcher.AnswerPermissionAsync(run.Id, requestId, new CliPermissionAnswerDto
        {
            OptionId = CliInteractionOptionIds.Decline, Message = "Pick for me."
        })).Succeeded.ShouldBeTrue();
        await execution.WaitAsync(TestTimeout);

        var row = await RequestRowAsync(requestId);
        row.Status.ShouldBe(CliPermissionStatus.Denied);
        row.AnswersJson.ShouldBeNull();
        var written = remote.Stdin.Single(l => l.Contains("control_response"));
        written.ShouldContain("\"behavior\":\"deny\"");
        written.ShouldContain("Pick for me.");
    }

    /// <summary>没人回答的问题按期限结束：agent 收到「没有回答、别猜」，而不是一份空回答。</summary>
    [Fact]
    public async Task AQuestionNobodyAnswers_EndsAtItsDeadline_WithoutAnswers()
    {
        var remote = Remote("claude-ask-user-question.jsonl");
        var host = await CreateHostAsync(remote, CliPermissionMode.Full);
        var run = await SeedClaimedRunAsync(attended: true);

        await CreateExecutor(host, timeout: TimeSpan.FromMilliseconds(300)).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var written = remote.Stdin.Single(l => l.Contains("control_response"));
        written.ShouldContain("\"behavior\":\"deny\"");
        written.ShouldContain("Do not assume an answer");
        written.ShouldNotContain("\"answers\"");
        var outcome = CliRunMessageConverter.DeserializePermission(
            (await MessagesAsync(run.Id)).Single(m => m.Type == CliAgentEventType.PermissionResolved).PermissionJson)!;
        outcome.Status.ShouldBe(CliPermissionStatus.TimedOut);
        outcome.Answers.ShouldBeNull();
    }

    /// <summary>没有人值守：CLI 不带交给宿主的开关、提问工具被拿掉；万一问到也当场拒绝，不建请求行。</summary>
    [Fact]
    public async Task AnUnattendedRun_HasNoQuestionTool_AndAQuestionIsDeniedWithoutARequest()
    {
        var remote = Remote("claude-ask-user-question.jsonl");
        var host = await CreateHostAsync(remote, CliPermissionMode.Full);
        var run = await SeedClaimedRunAsync(attended: false);

        await CreateExecutor(host).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var args = _launched.Single().Arguments;
        args.ShouldNotContain("--permission-prompt-tool");
        args.ShouldContain("AskUserQuestion");
        remote.Stdin.Single(l => l.Contains("control_response")).ShouldContain("\"behavior\":\"deny\"");
        (await MessagesAsync(run.Id)).ShouldNotContain(m => m.Type == CliAgentEventType.PermissionRequest);
        (await DbContext.Set<CliRunPermissionRequest>().IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    /// <summary>服务端不在的时候用户答了问题：接回来之后重放追上已落库的事件，再把同一份回答写给 CLI（只写一次）。</summary>
    [Fact]
    public async Task AQuestionAnsweredWhileTheServerWasDown_IsWrittenOnceAfterReattaching()
    {
        var remote = Remote("claude-ask-user-question.jsonl", handle: "proc-question");
        var host = await CreateHostAsync(remote, CliPermissionMode.Full);
        var run = await SeedClaimedRunAsync(attended: true);

        Guid requestId;
        using (var shutdown = new CancellationTokenSource())
        {
            var execution = CreateExecutor(host).ExecuteAsync(run.Id, shutdown.Token);
            requestId = CliRunMessageConverter.DeserializePermission((await WaitForRequestAsync(run.Id)).PermissionJson)!.RequestId!.Value;
            await shutdown.CancelAsync();
            await execution.WaitAsync(TestTimeout);
        }

        (await RequestRowAsync(requestId)).Status.ShouldBe(CliPermissionStatus.Pending);
        (await CreateDispatcher().AnswerPermissionAsync(run.Id, requestId, new CliPermissionAnswerDto
        {
            OptionId = CliInteractionOptionIds.Submit,
            Answers = new() { [ColorQuestion] = ["Blue"], [ExtrasQuestion] = ["Dots"] }
        })).Succeeded.ShouldBeTrue();

        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id);
        host.OnReattach = _ => CliReattachResult.Attached(remote.Connect());
        await CreateExecutor(host).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        remote.Stdin.Single(l => l.Contains("control_response")).ShouldContain("\"Which color do you prefer?\":\"Blue\"");
        var messages = await MessagesAsync(run.Id);
        messages.Select(m => m.Sequence).ShouldBe(Enumerable.Range(1, messages.Count));
        messages.Count(m => m.Type == CliAgentEventType.PermissionResolved).ShouldBe(1);
    }

    /// <summary>回答不限于选项标签（claude 本机界面的 "Other"）：用户自己写的一句照样收下，按问题顺序规整。</summary>
    [Fact]
    public void AnAnswerOfTheUsersOwn_IsAccepted()
    {
        CliQuestion[] questions =
        [
            new(ColorQuestion, "Color", [new("Red", null), new("Blue", null)], false),
            new(ExtrasQuestion, "Extras", [new("Stripes", null)], true)
        ];

        var (answers, problem) = CliAgentDispatcher.NormalizeAnswers(questions, new()
        {
            [ExtrasQuestion] = ["Stripes", "Stripes", "A star on top"],
            [ColorQuestion] = ["Something green"]
        });

        problem.ShouldBeNull();
        answers!.Keys.ShouldBe([ColorQuestion, ExtrasQuestion]);
        answers[ColorQuestion].ShouldBe(["Something green"]);
        answers[ExtrasQuestion].ShouldBe(["Stripes", "A star on top"]);
    }

    // ── 计划 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 以计划档启动、有人值守：CLI 带 <c>--permission-mode plan</c>；计划以请求到达，「批准并自动接受改动」切换会话的档位，
    /// 批准之后要权限的命令照常交给使用方。
    /// </summary>
    [Fact]
    public async Task APlanRun_HandsThePlanToTheUser_AndAfterApprovalCommandsStillAsk()
    {
        var remote = Remote("claude-plan-approved-accept-edits.jsonl");
        var host = await CreateHostAsync(remote, CliPermissionMode.Plan);
        var run = await SeedClaimedRunAsync(attended: true);

        var execution = CreateExecutor(host).ExecuteAsync(run.Id, CancellationToken.None);
        var plan = CliRunMessageConverter.DeserializePermission((await WaitForRequestAsync(run.Id, execution: execution)).PermissionJson)!;
        plan.Kind.ShouldBe(CliInteractionKinds.Plan);
        plan.Plan!.ShouldStartWith("# Plan: create hello.py");
        plan.Options.Select(o => o.Id).ShouldContain(CliInteractionOptionIds.ApproveAcceptEdits);

        (await CreateDispatcher().AnswerPermissionAsync(run.Id, plan.RequestId!.Value,
            new CliPermissionAnswerDto { OptionId = CliInteractionOptionIds.ApproveAcceptEdits })).Succeeded.ShouldBeTrue();

        var command = await WaitForRequestAsync(run.Id, index: 1, execution: execution);
        command.Tool.ShouldBe("PowerShell");
        var commandRequest = CliRunMessageConverter.DeserializePermission(command.PermissionJson)!;
        commandRequest.Kind.ShouldBe(CliInteractionKinds.Permission);
        (await CreateDispatcher().AnswerPermissionAsync(run.Id, commandRequest.RequestId!.Value,
            new CliPermissionAnswerDto { OptionId = CliPermissionOptionKinds.AllowOnce })).Succeeded.ShouldBeTrue();

        await execution.WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        var args = _launched.Single().Arguments;
        args.ShouldContain("plan");
        args.ShouldContain("--permission-prompt-tool");
        remote.Stdin.First(l => l.Contains("control_response")).ShouldContain("\"mode\":\"acceptEdits\"");
        (await RequestRowAsync(plan.RequestId.Value)).Status.ShouldBe(CliPermissionStatus.Allowed);
    }

    /// <summary>不批准计划：意见交给 agent（落在请求行上），回答体里多给的 answers 不存。</summary>
    [Fact]
    public async Task RejectingAPlan_HandsTheFeedbackToTheAgent()
    {
        var remote = Remote("claude-plan-rejected.jsonl");
        var host = await CreateHostAsync(remote, CliPermissionMode.Plan);
        var run = await SeedClaimedRunAsync(attended: true);

        var execution = CreateExecutor(host, timeout: TimeSpan.FromSeconds(30)).ExecuteAsync(run.Id, CancellationToken.None);
        var first = CliRunMessageConverter.DeserializePermission((await WaitForRequestAsync(run.Id, execution: execution)).PermissionJson)!;
        (await CreateDispatcher().AnswerPermissionAsync(run.Id, first.RequestId!.Value, new CliPermissionAnswerDto
        {
            OptionId = CliInteractionOptionIds.Reject,
            Message = "Please also add a README step to the plan.",
            Answers = new() { ["x"] = ["y"] }
        })).Succeeded.ShouldBeTrue();

        // agent 改完计划又交了一次：这次也不批准，这一轮结束
        var second = CliRunMessageConverter.DeserializePermission((await WaitForRequestAsync(run.Id, index: 1, execution: execution)).PermissionJson)!;
        (await CreateDispatcher().AnswerPermissionAsync(run.Id, second.RequestId!.Value,
            new CliPermissionAnswerDto { OptionId = CliInteractionOptionIds.Reject, Message = "Please also add a README step to the plan." }))
            .Succeeded.ShouldBeTrue();
        await execution.WaitAsync(TestTimeout);

        var row = await RequestRowAsync(first.RequestId.Value);
        row.Status.ShouldBe(CliPermissionStatus.Denied);
        row.Message.ShouldBe("Please also add a README step to the plan.");
        row.AnswersJson.ShouldBeNull();
        remote.Stdin.First(l => l.Contains("control_response")).ShouldContain("Please also add a README step to the plan.");
    }

    // ── 收窄了内置工具 ───────────────────────────────────────────────────────

    /// <summary>
    /// 档案用 BuiltInTools 收窄内置工具（<c>--tools WebSearch</c>）：有人值守时提问与计划工具补进清单，没有人值守时不补。
    /// 实测 claude 2.1.317 不补时这三个工具从 init 帧的工具列表里一起消失。
    /// </summary>
    [Theory]
    [InlineData(true, "WebSearch,AskUserQuestion,EnterPlanMode,ExitPlanMode")]
    [InlineData(false, "WebSearch")]
    public async Task ARestrictedToolList_KeepsTheInteractionTools_OnlyWhenSomeoneAttends(bool attended, string expected)
    {
        var remote = Remote("claude-ask-user-question.jsonl");
        var host = await CreateHostAsync(remote, CliPermissionMode.Full);
        host.LaunchProfile = _ => new CliLaunchProfile { PermissionMode = CliPermissionMode.Full, BuiltInTools = ["WebSearch"] };
        var run = await SeedClaimedRunAsync(attended);

        var execution = CreateExecutor(host, timeout: TimeSpan.FromMilliseconds(300)).ExecuteAsync(run.Id, CancellationToken.None);
        await execution.WaitAsync(TestTimeout);

        var args = _launched.Single().Arguments.ToList();
        var tools = args.IndexOf("--tools");
        tools.ShouldBeGreaterThanOrEqualTo(0);
        args[tools + 1].ShouldBe(expected);
    }

    /// <summary>补工具只补缺的：消费方自己已经列了的不重复；档案没收窄时不加 <c>--tools</c>（CLI 自带的全部工具本就包括它们）。</summary>
    [Fact]
    public void TheInteractionTools_AreAddedOnce_AndOnlyToARestrictedList()
    {
        var claude = CliBuiltInProviders.All["claude"];
        var run = new CliRun { Attended = true };

        CliRunExecutor.WithInteractionTools(new CliLaunchProfile { BuiltInTools = ["WebSearch", "ExitPlanMode"] }, claude, run)
            .BuiltInTools.ShouldBe(["WebSearch", "ExitPlanMode", "AskUserQuestion", "EnterPlanMode"]);
        CliRunExecutor.WithInteractionTools(new CliLaunchProfile { BuiltInTools = [] }, claude, run)
            .BuiltInTools.ShouldBe(["AskUserQuestion", "EnterPlanMode", "ExitPlanMode"]);
        CliRunExecutor.WithInteractionTools(new CliLaunchProfile(), claude, run).BuiltInTools.ShouldBeNull();

        // 档案说没人值守，或 provider 交不出这些请求：不补
        CliRunExecutor.WithInteractionTools(new CliLaunchProfile { BuiltInTools = ["WebSearch"], Attended = false }, claude, run)
            .BuiltInTools.ShouldBe(["WebSearch"]);
        CliRunExecutor.WithInteractionTools(new CliLaunchProfile { BuiltInTools = ["WebSearch"] }, claude with { HostPromptArgs = null }, run)
            .BuiltInTools.ShouldBe(["WebSearch"]);
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    /// <summary>一个回放录像的 claude 进程：收到提示交出第一段，每收到一条应答交出下一段。</summary>
    private static SimulatedRemoteProcess Remote(string recording, string? handle = null)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Adapters", "Recordings", recording);
        var segments = new List<List<string>> { new() };
        foreach (var line in File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            if (line == "#answer")
            {
                segments.Add([]);
            }
            else
            {
                segments[^1].Add(line);
            }
        }

        var next = 0;
        return new SimulatedRemoteProcess(handle, line =>
            (line.Contains("\"type\":\"user\"", StringComparison.Ordinal) && next == 0)
            || line.Contains("control_response", StringComparison.Ordinal)
                ? next < segments.Count ? segments[next++] : []
                : []);
    }

    private async Task<TestCliHost> CreateHostAsync(SimulatedRemoteProcess remote, CliPermissionMode mode)
    {
        var host = new TestCliHost(
            (spec, _) =>
            {
                _launched.Add(spec);
                return Task.FromResult<ICliProcess>(remote.Connect());
            },
            executablePath: RemoteExecutable,
            fileSystem: new InMemoryHostFileSystem(),
            workspacesRoot: RemoteRoot)
        {
            LaunchProfile = _ => new CliLaunchProfile { PermissionMode = mode }
        };

        var paths = host.FileSystem.Paths;
        await host.FileSystem.CreateDirectoryAsync(paths.GetDirectoryName(RemoteExecutable)!, CancellationToken.None);
        await host.FileSystem.WriteAllBytesAsync(RemoteExecutable, [0x7F, 0x45, 0x4C, 0x46], CancellationToken.None);
        return host;
    }

    private async Task<CliRun> SeedClaimedRunAsync(bool attended)
    {
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

        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = "ask me",
            Attended = attended
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task<CliRunMessage> WaitForRequestAsync(Guid runId, int index = 0, Task? execution = null)
    {
        CliRunMessage? request = null;
        try
        {
            await WaitUntilAsync(async () =>
            {
                request = (await MessagesAsync(runId)).Where(m => m.Type == CliAgentEventType.PermissionRequest).Skip(index).FirstOrDefault();
                return request is not null;
            });
        }
        catch (TimeoutException)
        {
            var run = await ReloadAsync(runId);
            var seen = string.Join(", ", (await MessagesAsync(runId)).Select(m => $"{m.Type}:{m.Tool}:{m.Status}"));
            throw new TimeoutException(
                $"Request #{index} never arrived. Run {run.Status} ({run.Error}); execution {execution?.Status} {execution?.Exception}; events: {seen}");
        }

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
