namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// agent 问用户问题（<c>AskUserQuestion</c>）与交出计划等批准（<c>ExitPlanMode</c>）：有人值守时适配器把它们交成
/// <see cref="CliAgentEventType.PermissionRequest"/> 事件（种类 question / plan），回答写给 CLI 的形状与真实 CLI 接受的一致；
/// 没有人值守时当场拒绝，绝不交给 agent 一份空回答。
/// </summary>
/// <remarks>
/// 主要的几条回放 <c>Adapters/Recordings/</c> 里录下的真实交换（claude 2.1.316，<c>--permission-prompt-tool stdio</c>）：
/// 以 <c>#answer</c> 分段，适配器每写出一条 control_response，假 transport 就交出下一段（CLI 收到回答之后真实写出的帧）。
/// </remarks>
public class InteractionRequestAdapterTests
{
    private const string ColorQuestion = "Which color do you prefer?";
    private const string ExtrasQuestion = "Which extras do you want? (select any)";
    private const string Result = """{"type":"result","subtype":"success","session_id":"s","result":"done","num_turns":1}""";

    private static CliAgentLaunchContext Context(
        CliPermissionMode mode = CliPermissionMode.Full, bool relays = true, bool asks = false, string provider = "claude") => new()
    {
        Provider = CliBuiltInProviders.All[provider],
        ExecutablePath = "/nonexistent/claude",
        Prompt = "hello",
        WorkingDirectory = "/tmp/does-not-need-to-exist",
        PermissionMode = mode,
        AsksForApproval = asks,
        RelaysInteractions = relays
    };

    /// <summary>按 <c>#answer</c> 分段读一份录像。</summary>
    private static List<List<string>> Recording(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Adapters", "Recordings", name);
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

        return segments;
    }

    /// <summary>回放录像：先交第一段，适配器每写出一条应答就交下一段。</summary>
    private static FakeCliAgentTransport Replay(string name)
    {
        var segments = Recording(name);
        var next = 1;
        return new FakeCliAgentTransport(segments[0], onWrite: line =>
            line.Contains("control_response", StringComparison.Ordinal) && next < segments.Count ? segments[next++] : []);
    }

    private static async Task<List<CliAgentEvent>> RunAnsweringAsync(
        ICliProtocolAdapter adapter, ICliAgentTransport transport, CliAgentLaunchContext context, Func<CliAgentEvent, CliPermissionDecision?> answer)
    {
        var events = new List<CliAgentEvent>();
        await foreach (var evt in adapter.RunAsync(transport, context, CancellationToken.None))
        {
            events.Add(evt);
            if (evt.Type == CliAgentEventType.PermissionRequest && answer(evt) is { } decision)
            {
                (await adapter.AnswerPermissionAsync(evt.Permission!.ProtocolKey!, decision, CancellationToken.None)).ShouldBeTrue();
            }
        }

        return events;
    }

    private static JsonElement Decision(FakeCliAgentTransport transport, int index = 0)
    {
        using var reply = JsonDocument.Parse(transport.Written.Where(w => w.Contains("control_response")).ElementAt(index));
        return reply.RootElement.GetProperty("response").GetProperty("response").Clone();
    }

    private static StreamJsonAdapter NewAdapter() => new(NullLogger<StreamJsonAdapter>.Instance);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Answers(params (string Question, string[] Values)[] answers)
        => answers.ToDictionary(a => a.Question, a => (IReadOnlyList<string>)a.Values, StringComparer.Ordinal);

    // ── 问题 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 录像：全权档（bypassPermissions）下 AskUserQuestion 照样以 can_use_tool 到达；请求带齐问题，回答写成
    /// 「原入参 + answers」，CLI 收下之后 agent 读到的正是这些回答。
    /// </summary>
    [Fact]
    public async Task Recording_AQuestionInAFullRun_ReachesTheUser_AndTheAnswersGoBackInTheShapeTheCliAccepted()
    {
        var transport = Replay("claude-ask-user-question.jsonl");
        var adapter = NewAdapter();
        var writesBeforeAnswer = -1;

        var events = await RunAnsweringAsync(adapter, transport, Context(), _ =>
        {
            writesBeforeAnswer = transport.Written.Count(w => w.Contains("control_response"));
            return new CliPermissionDecision(true, CliInteractionOptionIds.Submit, null,
                Answers((ColorQuestion, ["Red"]), (ExtrasQuestion, ["Stripes"])));
        });

        writesBeforeAnswer.ShouldBe(0, "nothing is answered before the user answers, even in the full mode");
        var request = events.Single(e => e.Type == CliAgentEventType.PermissionRequest);
        request.Tool.ShouldBe("AskUserQuestion");
        request.CallId.ShouldBe("toolu_01XbSxkP9fNuY4Hawc5BNL59");
        var permission = request.Permission!;
        permission.Kind.ShouldBe(CliInteractionKinds.Question);
        permission.Options.Select(o => o.Id).ShouldBe([CliInteractionOptionIds.Submit, CliInteractionOptionIds.Decline]);
        permission.Options.Select(o => o.Kind).ShouldBe([CliPermissionOptionKinds.AllowOnce, CliPermissionOptionKinds.RejectOnce]);
        permission.Questions!.Count.ShouldBe(2);
        permission.Questions[0].ShouldSatisfyAllConditions(
            q => q.Question.ShouldBe(ColorQuestion),
            q => q.Header.ShouldBe("Color"),
            q => q.MultiSelect.ShouldBeFalse(),
            q => q.Options.Select(o => o.Label).ShouldBe(["Red", "Blue"]),
            q => q.Options[0].Description.ShouldBe("The color red"));
        permission.Questions[1].MultiSelect.ShouldBeTrue();

        var decision = Decision(transport);
        decision.GetProperty("behavior").GetString().ShouldBe("allow");
        var updated = decision.GetProperty("updatedInput");
        updated.GetProperty("questions").GetArrayLength().ShouldBe(2, "the original input goes back with the answers added");
        updated.GetProperty("answers").GetProperty(ColorQuestion).GetString().ShouldBe("Red");
        updated.GetProperty("answers").GetProperty(ExtrasQuestion).GetString().ShouldBe("Stripes");

        // CLI 收下回答之后（录像的下一段）：agent 读到的工具结果就是这些回答，这一轮正常结束
        events.Single(e => e.Type == CliAgentEventType.ToolResult).Output!.ShouldContain("\"Which color do you prefer?\"=\"Red\"");
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0, StderrTail = "", Elapsed = TimeSpan.FromSeconds(1) })
            .Status.ShouldBe(CliRunStatus.Completed);
    }

    [Fact]
    public async Task MultipleAnswers_AreJoinedTheWayTheCliExpects()
    {
        var transport = Replay("claude-ask-user-question.jsonl");

        await RunAnsweringAsync(NewAdapter(), transport, Context(), _ => new CliPermissionDecision(true, CliInteractionOptionIds.Submit, null,
            Answers((ColorQuestion, ["Blue"]), (ExtrasQuestion, ["Stripes", "Dots"]))));

        Decision(transport).GetProperty("updatedInput").GetProperty("answers").GetProperty(ExtrasQuestion).GetString().ShouldBe("Stripes, Dots");
    }

    [Fact]
    public async Task DecliningTheQuestions_TellsTheAgentNotToAssumeAnswers()
    {
        var transport = Replay("claude-ask-user-question.jsonl");

        await RunAnsweringAsync(NewAdapter(), transport, Context(), _ => new CliPermissionDecision(false, CliInteractionOptionIds.Decline, null));

        var decision = Decision(transport);
        decision.GetProperty("behavior").GetString().ShouldBe("deny");
        decision.GetProperty("message").GetString()!.ShouldContain("Do not assume answers");
    }

    /// <summary>「允许」却没带回答（超时、代码缺陷）：按不答处理，绝不写一份空的 answers 让 agent 去猜。</summary>
    [Fact]
    public async Task AnAllowWithoutAnswers_IsWrittenAsADenial_NeverAsEmptyAnswers()
    {
        var transport = Replay("claude-ask-user-question.jsonl");

        await RunAnsweringAsync(NewAdapter(), transport, Context(), _ => new CliPermissionDecision(true, CliInteractionOptionIds.Submit, null));

        Decision(transport).GetProperty("behavior").GetString().ShouldBe("deny");
    }

    /// <summary>没有人值守：问题当场拒绝、不产出请求事件；全权档也不自动放行（那会让 agent 拿到空回答去猜）。</summary>
    [Theory]
    [InlineData(CliPermissionMode.Full)]
    [InlineData(CliPermissionMode.Ask)]
    public async Task Unattended_AQuestionIsDeniedOnTheSpot_InEveryMode(CliPermissionMode mode)
    {
        var transport = Replay("claude-ask-user-question.jsonl");

        var events = await RunAnsweringAsync(NewAdapter(), transport, Context(mode, relays: false), _ => null);

        events.ShouldNotContain(e => e.Type == CliAgentEventType.PermissionRequest);
        var decision = Decision(transport);
        decision.GetProperty("behavior").GetString().ShouldBe("deny");
        decision.GetProperty("message").GetString()!.ShouldContain("No one is attending this run");
    }

    // ── 计划 ─────────────────────────────────────────────────────────────────

    /// <summary>录像：计划档里 agent 交出计划，拒绝时附的意见原样成为 agent 读到的工具结果，它接着改计划、再交一次。</summary>
    [Fact]
    public async Task Recording_RejectingThePlan_HandsTheFeedbackToTheAgent()
    {
        var transport = Replay("claude-plan-rejected.jsonl");
        const string feedback = "Please also add a README step to the plan.";

        var events = await RunAnsweringAsync(NewAdapter(), transport, Context(CliPermissionMode.Plan, asks: true),
            _ => new CliPermissionDecision(false, CliInteractionOptionIds.Reject, feedback));

        var requests = events.Where(e => e.Type == CliAgentEventType.PermissionRequest).ToList();
        requests.Count.ShouldBe(2, "the agent revised the plan and asked again");
        var plan = requests[0].Permission!;
        plan.Kind.ShouldBe(CliInteractionKinds.Plan);
        plan.Plan!.ShouldStartWith("# Plan: hello.py");
        plan.PlanSource.ShouldBe(CliPlanSources.Input, "the CLI handed over the plan it wrote to its plan file");
        plan.Options.Select(o => o.Id).ShouldBe(
            [CliInteractionOptionIds.Approve, CliInteractionOptionIds.ApproveAcceptEdits, CliInteractionOptionIds.Reject]);
        requests[0].Input.ShouldBeNull("the plan is carried on the permission, not repeated as raw input");

        var decision = Decision(transport);
        decision.GetProperty("behavior").GetString().ShouldBe("deny");
        decision.GetProperty("message").GetString().ShouldBe(feedback);
        events.First(e => e.Type == CliAgentEventType.ToolResult && e.Output == feedback).Status.ShouldBe("failed");
    }

    /// <summary>
    /// 录像：「批准并自动接受改动」带上切到 acceptEdits 的规则（CLI 随即报 permissionMode: acceptEdits）；
    /// 批准之后要权限的命令照常以权限请求交给使用方。
    /// </summary>
    [Fact]
    public async Task Recording_ApprovingThePlanWithAutoAcceptedEdits_SwitchesTheSession_AndLaterCommandsStillAsk()
    {
        var transport = Replay("claude-plan-approved-accept-edits.jsonl");

        var events = await RunAnsweringAsync(NewAdapter(), transport, Context(CliPermissionMode.Plan, asks: true), evt =>
            evt.Permission!.Kind == CliInteractionKinds.Plan
                ? new CliPermissionDecision(true, CliInteractionOptionIds.ApproveAcceptEdits, null)
                : new CliPermissionDecision(true, CliPermissionOptionKinds.AllowOnce, null));

        var plan = Decision(transport, 0);
        plan.GetProperty("behavior").GetString().ShouldBe("allow");
        plan.GetProperty("updatedInput").GetProperty("plan").GetString()!.ShouldStartWith("# Plan: create hello.py");
        var rule = plan.GetProperty("updatedPermissions")[0];
        rule.GetProperty("type").GetString().ShouldBe("setMode");
        rule.GetProperty("mode").GetString().ShouldBe("acceptEdits");
        rule.GetProperty("destination").GetString().ShouldBe("session");

        var command = events.Where(e => e.Type == CliAgentEventType.PermissionRequest).Last();
        command.Tool.ShouldBe("PowerShell");
        command.Permission!.Kind.ShouldBe(CliInteractionKinds.Permission);
        Decision(transport, 1).GetProperty("behavior").GetString().ShouldBe("allow");
    }

    /// <summary>不是以计划档启动的运行（agent 自己进的计划）：只有批准与不批准，批准不改档位（CLI 回到原来的档位）。</summary>
    [Fact]
    public async Task APlanInAFullRun_OffersApproveAndReject_AndApprovingKeepsTheMode()
    {
        var transport = Replay("claude-plan-approved-accept-edits.jsonl");

        var events = await RunAnsweringAsync(NewAdapter(), transport, Context(CliPermissionMode.Full), evt =>
            new CliPermissionDecision(true, CliInteractionOptionIds.Approve, null));

        events.First(e => e.Type == CliAgentEventType.PermissionRequest).Permission!.Options.Select(o => o.Id)
            .ShouldBe([CliInteractionOptionIds.Approve, CliInteractionOptionIds.Reject]);
        Decision(transport, 0).TryGetProperty("updatedPermissions", out _).ShouldBeFalse();

        // 全权档下普通命令的 can_use_tool 照旧当场放行，不交给使用方
        events.Count(e => e.Type == CliAgentEventType.PermissionRequest).ShouldBe(1);
        Decision(transport, 1).GetProperty("behavior").GetString().ShouldBe("allow");
    }

    [Fact]
    public async Task Unattended_APlanIsDeniedOnTheSpot_AndTheAgentIsToldToGiveThePlanAsItsReply()
    {
        var transport = new FakeCliAgentTransport(
            [Recording("claude-plan-rejected.jsonl")[0].Last()], onWrite: line => line.Contains("control_response") ? [Result] : []);

        var events = await RunAnsweringAsync(NewAdapter(), transport, Context(CliPermissionMode.Plan, relays: false), _ => null);

        events.ShouldNotContain(e => e.Type == CliAgentEventType.PermissionRequest);
        Decision(transport).GetProperty("message").GetString()!.ShouldContain("give the plan as your reply");
    }

    /// <summary>
    /// 录像（claude 2.1.317，<c>--tools WebSearch,AskUserQuestion,EnterPlanMode,ExitPlanMode</c>，没有 Write）：计划文件写不出来，
    /// ExitPlanMode 的入参是 <c>{}</c>，计划只在之前的助手文字里。请求用那段文字补上并说明来源；被打回之后 agent 只在思考里改、
    /// 不写新文字就再交一次，第二个请求照样带着计划（不是空的）。
    /// </summary>
    [Fact]
    public async Task Recording_APlanWithoutAPlanFile_IsTakenFromWhatTheAgentWroteSincePlanModeBegan()
    {
        var transport = Replay("claude-plan-without-write.jsonl");
        var plans = 0;

        var events = await RunAnsweringAsync(NewAdapter(), transport, Context(CliPermissionMode.Plan, asks: true), evt =>
            evt.Permission!.Kind != CliInteractionKinds.Plan ? new CliPermissionDecision(true, CliPermissionOptionKinds.AllowOnce, null)
            : ++plans == 1 ? new CliPermissionDecision(false, CliInteractionOptionIds.Reject, "Please add a step that checks the result.")
            : new CliPermissionDecision(true, CliInteractionOptionIds.Approve, null));

        var requests = events.Where(e => e.Type == CliAgentEventType.PermissionRequest && e.Permission!.Kind == CliInteractionKinds.Plan)
            .Select(e => e.Permission!).ToList();
        requests.Count.ShouldBe(2);
        foreach (var request in requests)
        {
            request.PlanSource.ShouldBe(CliPlanSources.AssistantText);
            request.Plan.ShouldNotBeNull().ShouldContain("## Plan: compare the three most popular Python web frameworks");
        }

        // 批准之后（CLI 报回 default）写的文字不再算进计划
        Decision(transport, 0).GetProperty("behavior").GetString().ShouldBe("deny");
        Decision(transport, 1).GetProperty("updatedInput").EnumerateObject().ShouldBeEmpty("the CLI's own input goes back unchanged");
        requests[1].Plan!.ShouldNotContain("Here's the comparison.");
    }

    /// <summary>
    /// 录像（claude 2.1.317，没有 Write）：agent 在工具调用里传了 <c>plan</c>，CLI 发来的批准请求的入参却是 <c>{}</c>，计划取自那次工具调用
    /// （来源仍是 <see cref="CliPlanSources.Input"/>）。被退回之后它没写回复文字，计划只出现在 CLI 发出的思考里：第二个请求取思考。
    /// </summary>
    [Fact]
    public async Task Recording_APlanTheCliDropsFromTheRequest_IsTakenFromTheToolCall_ThenFromThinking()
    {
        var transport = Replay("claude-plan-sent-back.jsonl");
        var plans = 0;

        var events = await RunAnsweringAsync(NewAdapter(), transport, Context(CliPermissionMode.Plan, asks: true), _ =>
            ++plans == 1
                ? new CliPermissionDecision(false, CliInteractionOptionIds.Reject, "Write it out in your reply.")
                : new CliPermissionDecision(true, CliInteractionOptionIds.Approve, null));

        var requests = events.Where(e => e.Type == CliAgentEventType.PermissionRequest).Select(e => e.Permission!).ToList();
        requests.Count.ShouldBe(2, "the first request was not sent back: the plan was in the tool call");
        requests[0].PlanSource.ShouldBe(CliPlanSources.Input);
        requests[0].Plan!.ShouldStartWith("## Context\nCompare the three most popular Python web frameworks");
        requests[1].PlanSource.ShouldBe(CliPlanSources.AssistantThinking);
        requests[1].Plan!.ShouldStartWith("I'm comparing Django, Flask and FastAPI");
        Decision(transport, 0).GetProperty("message").GetString().ShouldBe("Write it out in your reply.");
    }

    /// <summary>
    /// agent 自己进计划（全权档里调 EnterPlanMode，CLI 报 <c>status: plan</c>）：之前写的文字不算计划，子 agent 写的也不算，
    /// 离开计划之后再进一次从头记。
    /// </summary>
    [Fact]
    public async Task ThePlanTextStartsWhenPlanModeBegins_AndLeavesOutSubAgents()
    {
        static string Status(string mode) => new JsonObject { ["type"] = "system", ["subtype"] = "status", ["permissionMode"] = mode }.ToJsonString();
        static string Text(string text, string? parent = null) => new JsonObject
        {
            ["type"] = "assistant",
            ["parent_tool_use_id"] = parent,
            ["message"] = new JsonObject
            {
                ["role"] = "assistant",
                ["model"] = "m",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text })
            }
        }.ToJsonString();
        static string ExitPlan(string id) => new JsonObject
        {
            ["type"] = "control_request",
            ["request_id"] = id,
            ["request"] = new JsonObject
            {
                ["subtype"] = "can_use_tool", ["tool_name"] = "ExitPlanMode", ["input"] = new JsonObject(), ["tool_use_id"] = $"t-{id}"
            }
        }.ToJsonString();

        var transport = new FakeCliAgentTransport(
            [
                """{"type":"system","subtype":"init","session_id":"s","permissionMode":"bypassPermissions"}""",
                Text("Let me look around first."),
                Status("plan"),
                Text("Step 1: read the code."),
                Text("Sub-agent notes: the code has three modules.", parent: "toolu_task"),
                Text("Step 2: write the comparison."),
                ExitPlan("p1")
            ],
            onWrite: line => line.Contains("\"p1\"")
                ? [Status("bypassPermissions"), Text("Working on it."), Status("plan"), Text("A second, separate plan."), ExitPlan("p2")]
                : line.Contains("\"p2\"") ? [Result] : []);

        var events = await RunAnsweringAsync(NewAdapter(), transport, Context(CliPermissionMode.Full), _ =>
            new CliPermissionDecision(true, CliInteractionOptionIds.Approve, null));

        var plans = events.Where(e => e.Type == CliAgentEventType.PermissionRequest).Select(e => e.Permission!.Plan).ToList();
        plans.ShouldBe(["Step 1: read the code.\n\nStep 2: write the comparison.", "A second, separate plan."]);
    }

    /// <summary>
    /// 计划档里 agent 什么都没写就交了个空计划（真机实测 2.1.317：没有 Write 时计划只在思考里）：先退回给 agent 一次，让它把计划写进
    /// <c>plan</c> 再交（不建请求、人看不到这张白纸）；它照做了，请求带着计划到达使用方。
    /// </summary>
    [Fact]
    public async Task AnEmptyPlan_IsSentBackOnce_AndTheRewrittenOneReachesTheUser()
    {
        var transport = new FakeCliAgentTransport(
            [ExitPlan("p1", "{}")],
            onWrite: line => line.Contains("\"p1\"") ? [ExitPlan("p2", """{"plan":"1. Read. 2. Compare."}""")]
                : line.Contains("\"p2\"") ? [Result] : []);

        var events = await RunAnsweringAsync(NewAdapter(), transport, Context(CliPermissionMode.Plan, asks: true), _ =>
            new CliPermissionDecision(true, CliInteractionOptionIds.Approve, null));

        var sentBack = Decision(transport, 0);
        sentBack.GetProperty("behavior").GetString().ShouldBe("deny");
        sentBack.GetProperty("message").GetString().ShouldBe(StreamJsonAdapter.MissingPlanMessage);
        var plan = events.Single(e => e.Type == CliAgentEventType.PermissionRequest).Permission!;
        plan.Plan.ShouldBe("1. Read. 2. Compare.");
        plan.PlanSource.ShouldBe(CliPlanSources.Input);
    }

    /// <summary>退回之后还是空的：照样交给使用方（计划与来源为 null，界面自己说「agent 没有给出计划」），不来回打转。</summary>
    [Fact]
    public async Task AnEmptyPlanAgain_ReachesTheUserWithoutAPlan_InsteadOfLooping()
    {
        var transport = new FakeCliAgentTransport(
            [ExitPlan("p1", "{}")],
            onWrite: line => line.Contains("\"p1\"") ? [ExitPlan("p2", "{}")] : line.Contains("\"p2\"") ? [Result] : []);

        var events = await RunAnsweringAsync(NewAdapter(), transport, Context(CliPermissionMode.Plan, asks: true), _ =>
            new CliPermissionDecision(true, CliInteractionOptionIds.Approve, null));

        transport.Written.Count(w => w.Contains("control_response")).ShouldBe(2);
        var plan = events.Single(e => e.Type == CliAgentEventType.PermissionRequest).Permission!;
        plan.Plan.ShouldBeNull();
        plan.PlanSource.ShouldBeNull();
    }

    private static string ExitPlan(string id, string inputJson) => new JsonObject
    {
        ["type"] = "control_request",
        ["request_id"] = id,
        ["request"] = new JsonObject
        {
            ["subtype"] = "can_use_tool", ["tool_name"] = "ExitPlanMode", ["input"] = JsonNode.Parse(inputJson), ["tool_use_id"] = $"t-{id}"
        }
    }.ToJsonString();

    // ── 上下文窗口 ───────────────────────────────────────────────────────────

    /// <summary>录像：上下文占用 = 主对话最后一次调用的输入（含缓存读写）加输出，窗口取 result.modelUsage 的 contextWindow。</summary>
    [Fact]
    public async Task Recording_TheContextUsageIsTheLastMainCall_WithTheWindowTheCliReported()
    {
        var adapter = NewAdapter();
        await RunAnsweringAsync(adapter, Replay("claude-ask-user-question.jsonl"), Context(), _ =>
            new CliPermissionDecision(true, CliInteractionOptionIds.Submit, null, Answers((ColorQuestion, ["Red"]), (ExtrasQuestion, ["Dots"]))));

        var context = adapter.GetResult(new CliSessionOutcome { ExitCode = 0, StderrTail = "", Elapsed = TimeSpan.FromSeconds(1) }).Context;

        context.ShouldNotBeNull();
        context.Tokens.ShouldBe(2 + 69072 + 2051 + 37);
        context.Window.ShouldBe(1_000_000);
        context.Model.ShouldBe("claude-opus-5-5");
    }

    /// <summary>子 agent（Task 工具里）的消息有自己的上下文，不算主对话的占用；没有 contextWindow 时窗口为 null。</summary>
    [Fact]
    public async Task ASubAgentsCalls_DoNotCountAsTheMainContext()
    {
        string Assistant(string id, long input, string? parent) => new JsonObject
        {
            ["type"] = "assistant",
            ["parent_tool_use_id"] = parent,
            ["message"] = new JsonObject
            {
                ["id"] = id,
                ["model"] = "m",
                ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "x" }),
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = input, ["output_tokens"] = 1, ["cache_read_input_tokens"] = 0, ["cache_creation_input_tokens"] = 0
                }
            }
        }.ToJsonString();
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport([Assistant("a", 100, null), Assistant("b", 900, "toolu_task"), Result]);

        await foreach (var _ in adapter.RunAsync(transport, Context(), CancellationToken.None))
        {
        }

        var context = adapter.GetResult(new CliSessionOutcome { ExitCode = 0, StderrTail = "", Elapsed = TimeSpan.FromSeconds(1) }).Context!;
        context.Tokens.ShouldBe(101);
        context.Window.ShouldBeNull();
    }

    // ── 启动参数 ─────────────────────────────────────────────────────────────

    [Fact]
    public void Attended_TheCliHandsPromptsToTheHost_AndKeepsItsQuestionTool()
    {
        var args = NewAdapter().BuildProcess(Context()).Arguments;

        ArgsContain(args, "--permission-prompt-tool", "stdio").ShouldBeTrue();
        args.ShouldNotContain("--disallowedTools");
    }

    [Fact]
    public void Unattended_NoPromptGoesToTheHost_AndTheQuestionToolIsRemoved()
    {
        var args = NewAdapter().BuildProcess(Context(relays: false)).Arguments;

        args.ShouldNotContain("--permission-prompt-tool");
        ArgsContain(args, "--disallowedTools", "AskUserQuestion").ShouldBeTrue();
    }

    /// <summary>
    /// 只转发权限请求（provider 能交给宿主、却不转发交互的那种组合）：权限提示照样交给框架（不带这个开关 claude 一条 can_use_tool 都不发），
    /// 提问工具仍然拿掉。
    /// </summary>
    [Fact]
    public void AskingForApprovalAlone_StillHandsPermissionPromptsToTheHost()
    {
        var args = NewAdapter().BuildProcess(Context(CliPermissionMode.Ask, relays: false, asks: true)).Arguments;

        ArgsContain(args, "--permission-prompt-tool", "stdio").ShouldBeTrue();
        ArgsContain(args, "--disallowedTools", "AskUserQuestion").ShouldBeTrue();
    }

    [Fact]
    public void ThePlanMode_IsClaudesPlanPermissionMode()
        => ArgsContain(NewAdapter().BuildProcess(Context(CliPermissionMode.Plan)).Arguments, "--permission-mode", "plan").ShouldBeTrue();

    /// <summary>谁回答权限提示由框架定：绑定或部署参数里的同名开关被丢弃。</summary>
    [Fact]
    public void CustomArgs_CannotTakeOverWhoAnswersPrompts()
    {
        var context = Context(relays: false) with { CustomArgs = ["--permission-prompt-tool", "mcp__evil__approve", "--permission-prompts", "none"] };

        var args = NewAdapter().BuildProcess(context).Arguments;

        args.ShouldNotContain("--permission-prompt-tool");
        args.ShouldNotContain("--permission-prompts");
        args.ShouldNotContain("mcp__evil__approve");
    }

    /// <summary>没有声明 <see cref="CliProviderDescriptor.HostPromptArgs"/> 的 stream-json provider：不带 claude 的开关，提问工具照旧拿掉。</summary>
    [Fact]
    public void AProviderWithoutHostPrompts_GetsNoPromptToolArgs()
    {
        var args = NewAdapter().BuildProcess(Context(relays: false, asks: true, mode: CliPermissionMode.Ask, provider: "qwen")).Arguments;

        args.ShouldNotContain("--permission-prompt-tool");
    }

    private static bool ArgsContain(IReadOnlyList<string> args, string name, string value)
        => args.Select((a, i) => (a, i)).Any(p => p.a == name && p.i + 1 < args.Count && args[p.i + 1] == value);
}
