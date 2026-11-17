using System.Text.RegularExpressions;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// stream-json 里子 agent（claude 的 <c>Agent</c> 工具）的帧：子 agent 的文字、思考、工具调用带上派出它的那次调用，
/// 它的开始、进展与结局成子 agent 事件，交付物与截断续接的位置只看主 agent。
/// </summary>
/// <remarks>
/// 回放 <c>Adapters/Recordings/</c> 里录下的真实 stdout（claude 2.1.319，haiku；路径、签名与账号信息已去掉）：
/// <list type="bullet">
/// <item><c>claude-subagent-foreground-nested.jsonl</c>：前台派一个子 agent，它读一个文件、再自己派一个子 agent 读另一个。
/// 第一层子 agent 的 assistant / user 帧带 <c>parent_tool_use_id</c>；第二层的帧 CLI 不发，只有它的
/// <c>task_started</c>（<c>parent_task_id</c>、<c>spawn_depth: 2</c>）/ <c>task_progress</c> / <c>task_notification</c>。</item>
/// <item><c>claude-subagent-background.jsonl</c>：<c>run_in_background</c> 派一个子 agent，主 agent 先答一句结束这一轮；
/// 子 agent 读完文件写下报告，<c>task_notification</c> 之后 CLI 自己开下一轮。录的时候用的是 <c>-p</c>，那种模式下第一轮的
/// result 压到最后才发；这里把它挪回 stdin 保持打开时的位置（紧跟第一轮的回答，见 <c>claude-background-task-follow-up.jsonl</c>）。</item>
/// <item><c>claude-subagent-stopped.jsonl</c>：后台派一个子 agent，随即用 <c>TaskStop</c> 停掉它：<c>task_notification</c> 的
/// <c>status</c> 是 <c>stopped</c>。</item>
/// </list>
/// </remarks>
public class StreamJsonSubAgentTests
{
    private const string OuterCall = "toolu_01CrxJifHDgGAuEFdoAGzgyQ";
    private const string InnerCall = "toolu_01L46M73LNMPjyxPxzhNmhrK";
    private const string BackgroundCall = "toolu_01XJsahMZpxvALW8tHmnKeC3";
    private const string StoppedCall = "toolu_01BNiUxWMJwrKN5B9Q8wCQGy";

    private static CliAgentLaunchContext Context(TimeSpan? wait = null) => new()
    {
        Provider = CliBuiltInProviders.All["claude"],
        ExecutablePath = "/nonexistent/claude",
        Prompt = "hello",
        WorkingDirectory = "/tmp/does-not-need-to-exist",
        BackgroundTaskWait = wait ?? TimeSpan.FromMinutes(30)
    };

    private static string[] Recording(string name)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Adapters", "Recordings", name))
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

    private static Task<(StreamJsonAdapter Adapter, List<CliAgentEvent> Events)> ReplayAsync(string recording, TimeSpan? wait = null)
        => ReplayAsync(Recording(recording), wait);

    private static async Task<(StreamJsonAdapter Adapter, List<CliAgentEvent> Events)> ReplayAsync(string[] lines, TimeSpan? wait = null)
    {
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var transport = new FakeCliAgentTransport(lines);
        var events = new List<CliAgentEvent>();
        await foreach (var evt in adapter.RunAsync(transport, Context(wait), CancellationToken.None))
        {
            events.Add(evt);
        }

        return (adapter, events);
    }

    private static List<CliAgentEvent> SubAgentTaskEvents(IEnumerable<CliAgentEvent> events, string callId)
        => events.Where(e => e.Type == CliAgentEventType.Task && e.CallId == callId).ToList();

    // ── 前台与嵌套 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 录像：子 agent 调的工具带上派出它的调用（嵌套派出的那次 Agent 调用也是第一层的），主 agent 只调了一次 Agent；
    /// 两层子 agent 各有开始与结局，第二层挂在第一层下面。
    /// </summary>
    [Fact]
    public async Task Recording_ForegroundAndNested_ToolCallsAreAttributedToTheirSubAgent()
    {
        var (_, events) = await ReplayAsync("claude-subagent-foreground-nested.jsonl");

        var toolUses = events.Where(e => e.Type == CliAgentEventType.ToolUse).Select(e => (e.Tool, e.CallId, e.ParentCallId)).ToList();
        toolUses.ShouldBe([("Agent", OuterCall, null), ("Read", "toolu_016ZPDU5RQdgAMtNT4QYwaym", OuterCall), ("Agent", InnerCall, OuterCall)]);

        var results = events.Where(e => e.Type == CliAgentEventType.ToolResult).ToDictionary(e => e.CallId!, e => e.ParentCallId);
        results["toolu_016ZPDU5RQdgAMtNT4QYwaym"].ShouldBe(OuterCall);
        results[InnerCall].ShouldBe(OuterCall);
        results[OuterCall].ShouldBeNull("the dispatch itself is the main agent's call");

        var outer = SubAgentTaskEvents(events, OuterCall);
        var started = outer.First();
        started.ParentCallId.ShouldBeNull();
        started.Task.ShouldNotBeNull().Status.ShouldBe(CliTaskStatuses.Running);
        started.Task.AgentType.ShouldBe("general-purpose");
        started.Task.Description.ShouldBe("Read alpha and beta files");
        started.Task.Background.ShouldBe(false);
        started.Task.Prompt.ShouldNotBeNull().ShouldStartWith("Read alpha.txt with the Read tool");
        started.Task.TaskId.ShouldBe("a9d5c37c9e58a18c8");
        started.Task.Kind.ShouldBe(CliTaskKinds.Agent);

        // The model is said once, from the sub-agent's first reply (task frames do not carry it); the event says nothing about the state
        var model = outer.Single(e => e.Task!.Model is not null).Task!;
        model.Model.ShouldBe("claude-haiku-5-5");
        model.Status.ShouldBeNull();
        SubAgentTaskEvents(events, InnerCall).ShouldAllBe(e => e.Task!.Model == null, "a nested sub-agent's replies are not streamed");

        var progress = outer.Where(e => e.Task!.Activity is not null).Select(e => e.Task!).ToList();
        progress.Select(p => p.Activity).ShouldBe(["Reading alpha.txt", "Read beta.txt and report content"]);
        progress[^1].ToolUseCount.ShouldBe(2);
        progress[^1].LastTool.ShouldBe("Agent");

        var finished = outer.Last().Task!;
        finished.Status.ShouldBe(CliTaskStatuses.Completed);
        finished.Summary.ShouldNotBeNull().ShouldContain("This file is alpha.");
        finished.DurationMs.ShouldBe(10697);
        finished.TotalTokens.ShouldBe(26301);

        var inner = SubAgentTaskEvents(events, InnerCall);
        inner.First().ParentCallId.ShouldBe(OuterCall, "the nested sub-agent hangs under the one that dispatched it");
        inner.First().Task!.Description.ShouldBe("Read beta.txt and report content");
        inner.Last().Task!.Status.ShouldBe(CliTaskStatuses.Completed);
    }

    /// <summary>录像：子 agent 的任务帧不再是一串空的 running 状态；task_updated 不出事件（结局由随后的通知带来）。</summary>
    [Fact]
    public async Task Recording_TaskFramesOfSubAgents_AreSubAgentEvents_NotBareStatuses()
    {
        var (_, events) = await ReplayAsync("claude-subagent-foreground-nested.jsonl");

        events.Count(e => e.Type == CliAgentEventType.Task).ShouldBe(2 + 3 + 2 + 1, "two starts, three progress frames, two notifications, the model");
        events.Count(e => e.Type == CliAgentEventType.Status && e.Status == "running")
            .ShouldBe(4, "only the init frame and the three thinking-token frames are left as running statuses");
    }

    /// <summary>录像：交付物与完整记录只有主 agent 的话；截断续接的位置是主会话的最后一个条目。</summary>
    [Fact]
    public async Task Recording_TheDeliverableIsTheMainAgentsAnswer()
    {
        var (adapter, events) = await ReplayAsync("claude-subagent-foreground-nested.jsonl");

        events.Where(e => e.Type == CliAgentEventType.Text).ShouldAllBe(e => e.ParentCallId == null);
        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed);
        result.Output.ShouldNotBeNull().ShouldStartWith("alpha.txt 内容为");
        result.SessionEntryId.ShouldBe("578d0023-123a-4f18-9d6d-f61e106a7a4c");
    }

    /// <summary>用量照旧：主对话与子 agent 的模型调用都算（result 的会话累计值），上下文占用只看主对话。</summary>
    [Fact]
    public async Task Recording_UsageStillCountsTheSubAgents_AndTheContextIsTheMainConversations()
    {
        var (adapter, _) = await ReplayAsync("claude-subagent-foreground-nested.jsonl");

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.UsageIsSessionTotal.ShouldBeTrue();
        var usage = result.Usage.ShouldHaveSingleItem();
        usage.Key.ShouldBe("claude-haiku-5-5");
        usage.Value.OutputTokens.ShouldBe(1634);
        usage.Value.CacheReadTokens.ShouldBe(132322);
        usage.Value.ReportedCostUsd.ShouldBe(0.01006947m);

        // 主对话最后一次调用：2 输入 + 1053 缓存写入 + 31327 缓存读取 + 1 输出
        result.Context.ShouldNotBeNull().Tokens.ShouldBe(2 + 1053 + 31327 + 1);
        result.Context.Window.ShouldBe(1000000);
    }

    // ── 后台 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 录像：后台子 agent 写的报告带着派出它的调用，不是回复文字；它的结局在第一轮结束之后到达，后台任务完成的状态照常有；
    /// 交付物是 CLI 自己开的那一轮的回答。
    /// </summary>
    [Fact]
    public async Task Recording_Background_TheReportIsTheSubAgents_AndItFinishesAfterTheTurnBoundary()
    {
        var (adapter, events) = await ReplayAsync("claude-subagent-background.jsonl");

        var report = events.Single(e => e.Type == CliAgentEventType.Text && e.Content!.Contains("alpha.txt 的内容只有一行"));
        report.ParentCallId.ShouldBe(BackgroundCall);
        events.Where(e => e.Type == CliAgentEventType.Text && e.ParentCallId == null).Select(e => e.Content).ToList()
            .ShouldBe(["Started. 后台任务已启动，正在读取 alpha.txt，完成后我会回报结果。", "alpha.txt 的内容是：`This file is alpha.`"]);

        var lifecycle = SubAgentTaskEvents(events, BackgroundCall);
        lifecycle.First().Task!.Background.ShouldBe(true);
        var finished = lifecycle.Last();
        finished.Task!.Status.ShouldBe(CliTaskStatuses.Completed);
        finished.Task.Summary.ShouldNotBeNull().ShouldContain("This file is alpha.");

        var boundary = events.FindIndex(e => e.Type == CliAgentEventType.TurnCompleted);
        boundary.ShouldBeGreaterThan(-1);
        events.IndexOf(finished).ShouldBeGreaterThan(boundary, "the background sub-agent finishes after the first answer");
        events.IndexOf(report).ShouldBeGreaterThan(boundary);
        var notice = events.Single(e => e.Type == CliAgentEventType.Status && e.Status == "background_task_finished");
        events.IndexOf(notice).ShouldBe(events.IndexOf(finished) + 1);

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Output.ShouldBe("alpha.txt 的内容是：`This file is alpha.`");
        result.SessionEntryId.ShouldBe("c3e10ca4-fa8d-4446-a2db-878cbff873d5");
    }

    /// <summary>
    /// 主 agent 的最后一轮没写字、只剩子 agent 的报告时，兜底答案不拿子 agent 的报告：它不是主 agent 的回答。
    /// </summary>
    [Fact]
    public async Task ASubAgentsReport_IsNeverTheFallbackAnswer()
    {
        // 去掉第二轮主 agent 的两帧（思考与回答），result 的 result 文本也置空：交付物只能来自兜底，而最后一条 assistant 帧是子 agent 的报告
        var lines = Recording("claude-subagent-background.jsonl")
            .Where(l => !l.Contains("c3e10ca4-fa8d-4446-a2db-878cbff873d5") && !l.Contains("9c68118b-7d40-4ce4-9f07-bd630380bc80"))
            .Select(l => l.Contains("\"result\":\"alpha.txt 的内容是") ? Regex.Replace(l, "\"result\":\"[^\"]*\"", "\"result\":\"\"") : l)
            .ToArray();

        var (adapter, _) = await ReplayAsync(lines);

        (adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Output ?? string.Empty).ShouldNotContain("alpha.txt 的内容只有一行");
    }

    /// <summary>录像：后台命令（<c>run_in_background</c> 的 Bash）也是任务：种类 command、在后台、说明是它的 description，结局带 CLI 写的退出说明。</summary>
    [Fact]
    public async Task Recording_ABackgroundCommand_IsATaskOfKindCommand()
    {
        var (_, events) = await ReplayAsync("claude-background-task-follow-up.jsonl");

        var tasks = events.Where(e => e.Type == CliAgentEventType.Task).ToList();
        tasks.Select(e => e.CallId).Distinct().ShouldHaveSingleItem().ShouldBe("toolu_01SpYwfeU2wF9ZUaUC6HHShi");
        var started = tasks.First().Task!;
        started.Kind.ShouldBe(CliTaskKinds.Command);
        started.Background.ShouldBe(true);
        started.Description.ShouldBe("Sleep then echo in background");
        started.Status.ShouldBe(CliTaskStatuses.Running);
        var finished = tasks.Last().Task!;
        finished.Status.ShouldBe(CliTaskStatuses.Completed);
        finished.Summary.ShouldBe("Background command \"Sleep then echo in background\" completed (exit code 0)");
    }

    // ── 停下、失败 ──────────────────────────────────────────────────────────

    /// <summary>录像：被 <c>TaskStop</c> 停掉的子 agent 以 stopped 结束；它调的工具与被打断的帧带着派出它的调用。</summary>
    [Fact]
    public async Task Recording_AStoppedSubAgent_EndsStopped()
    {
        var (_, events) = await ReplayAsync("claude-subagent-stopped.jsonl");

        events.Single(e => e.Type == CliAgentEventType.ToolUse && e.Tool == "PowerShell").ParentCallId.ShouldBe(StoppedCall);
        var failedCall = events.Single(e => e.Type == CliAgentEventType.ToolResult && e.CallId == "toolu_017E4j2dbTS74Hvg5wKfHYAD");
        failedCall.ParentCallId.ShouldBe(StoppedCall);
        failedCall.Status.ShouldBe("failed");
        SubAgentTaskEvents(events, StoppedCall).Last().Task!.Status.ShouldBe(CliTaskStatuses.Stopped);
    }

    /// <summary>通知说 failed 时子 agent 以 failed 结束。</summary>
    [Fact]
    public async Task ASubAgentThatFailed_EndsFailed()
    {
        var lines = Recording("claude-subagent-stopped.jsonl")
            .Select(l => l.Contains("\"subtype\":\"task_notification\"") ? l.Replace("\"status\":\"stopped\"", "\"status\":\"failed\"") : l)
            .ToArray();

        var (_, events) = await ReplayAsync(lines);

        SubAgentTaskEvents(events, StoppedCall).Last().Task!.Status.ShouldBe(CliTaskStatuses.Failed);
    }

    /// <summary>
    /// 主会话的最后一个条目之后还有子 agent 的帧（这里是它被打断时的那条）：截断续接的位置仍是主会话的条目，不是子 agent 记录里的。
    /// </summary>
    [Fact]
    public async Task ASubAgentsFrame_IsNeverWhereTheSessionIsCutBackTo()
    {
        // 去掉主 agent 最后的回答：剩下的最后一个 assistant / user 帧是子 agent 被打断的那条
        var lines = Recording("claude-subagent-stopped.jsonl").Where(l => !l.Contains("3863f418-ccfe-460e-b87a-9f806e7e9984")).ToArray();

        var (adapter, _) = await ReplayAsync(lines);

        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).SessionEntryId.ShouldBe("564cc0fd-36ad-42a4-84e0-d3565144dcf5");
    }

    // ── 计划档 ─────────────────────────────────────────────────────────────

    /// <summary>计划档里子 agent 的思考与文字带着派出它的调用，也不进计划（与主 agent 的分开）。</summary>
    [Fact]
    public async Task InPlanMode_ASubAgentsThinkingAndText_CarryTheirDispatch()
    {
        string Frame(string type, string block, string? parent) => $$"""
            {"type":"assistant","message":{"id":"m-{{Guid.NewGuid():N}}","model":"claude-haiku-5-5","content":[{{block}}]},"parent_tool_use_id":{{(parent is null ? "null" : $"\"{parent}\"")}},"session_id":"s","uuid":"{{Guid.NewGuid()}}"}
            """;
        var lines = new[]
        {
            """{"type":"system","subtype":"init","session_id":"s","permissionMode":"plan"}""",
            Frame("assistant", """{"type":"tool_use","id":"toolu_task","name":"Agent","input":{"description":"Survey","prompt":"Look around"}}""", null),
            Frame("assistant", """{"type":"thinking","thinking":"The sub-agent thinks."}""", "toolu_task"),
            Frame("assistant", """{"type":"text","text":"Sub-agent notes."}""", "toolu_task"),
            Frame("assistant", """{"type":"text","text":"Plan: do it."}""", null),
            """{"type":"result","subtype":"success","is_error":false,"result":"Plan: do it.","session_id":"s","num_turns":1}"""
        };

        var (_, events) = await ReplayAsync(lines);

        events.Single(e => e.Type == CliAgentEventType.Thinking).ParentCallId.ShouldBe("toolu_task");
        events.Where(e => e.Type == CliAgentEventType.Text).Select(e => (e.Content, e.ParentCallId)).ToList()
            .ShouldBe([("Sub-agent notes.", "toolu_task"), ("Plan: do it.", null)]);
    }
}
