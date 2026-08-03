namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// stream-json 适配器的事件归一化与终态判定。
/// </summary>
public class StreamJsonAdapterTests
{
    private static CliAgentLaunchContext Context(string? resumeSessionId = null) => new()
    {
        Provider = CliBuiltInProviders.All["claude"],
        ExecutablePath = "/nonexistent/claude",
        Prompt = "hello",
        WorkingDirectory = "/tmp/does-not-need-to-exist",
        ResumeSessionId = resumeSessionId
    };

    private static StreamJsonAdapter Adapter() => new(NullLogger<StreamJsonAdapter>.Instance);

    private static async Task<List<CliAgentEvent>> DrainAsync(
        StreamJsonAdapter adapter, FakeCliAgentTransport transport, CliAgentLaunchContext context)
    {
        var events = new List<CliAgentEvent>();
        await foreach (var evt in adapter.RunAsync(transport, context, CancellationToken.None))
        {
            events.Add(evt);
        }

        return events;
    }

    [Fact]
    public void BuildProcess_NeverPutsThePromptOnTheCommandLine()
    {
        // 提示词进命令行有两个后果：出现在同机其他用户可见的进程列表里，
        // 以及长提示撞上命令行长度上限。它必须走 stdin。
        var spec = Adapter().BuildProcess(Context() with { Prompt = "secret business context" });

        spec.Arguments.ShouldNotContain(a => a.Contains("secret business context"));
        spec.Arguments.ShouldContain("--output-format");
        spec.Arguments.ShouldContain("stream-json");
    }

    [Fact]
    public void BuildProcess_DropsProtocolCriticalCustomArgsAndTheirValues()
    {
        var spec = Adapter().BuildProcess(Context() with
        {
            CustomArgs = ["--output-format", "text", "--allowed-tools", "Bash"]
        });

        // 带值参数必须连值一起吞掉，否则 "text" 会作为裸位置参数漏给 CLI。
        spec.Arguments.ShouldNotContain("text");
        spec.Arguments.ShouldContain("--allowed-tools");
        spec.Arguments.ShouldContain("Bash");
    }

    [Fact]
    public void BuildProcess_StripsShellQuotesFromCustomArgs()
    {
        var spec = Adapter().BuildProcess(Context() with { CustomArgs = ["--deny-tool='write'"] });

        spec.Arguments.ShouldContain("--deny-tool=write");
    }

    [Fact]
    public async Task Run_TreatsResultEventAsTheOnlyTerminationSignal()
    {
        // 关键实测结论：因为 stdin 保持打开，CLI 不会自行退出，stdout 也不会关闭。
        // 等 EOF 的写法会让每个 turn 挂满超时窗口。这里的假 transport 刻意<b>不</b>结束流，
        // 只要适配器仍然返回，就证明它以 result 为终止信号。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"system","session_id":"sess-1"}""",
                """{"type":"assistant","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"OK"}]}}""",
                """{"type":"result","subtype":"success","session_id":"sess-1","result":"OK","num_turns":1}"""
            ],
            onWrite: _ => []);

        var adapter = Adapter();
        var events = await DrainAsync(adapter, transport, Context());

        events.ShouldContain(e => e.Type == CliAgentEventType.Text && e.Content == "OK");

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed);
        result.Output.ShouldBe("OK");
        result.SessionId.ShouldBe("sess-1");

        // 拿到终态后主动关 stdin，让 CLI 自行收尾退出。
        transport.InputClosed.ShouldBeTrue();
    }

    [Fact]
    public async Task Run_EmitsSessionIdEarlyOnTheSystemEvent()
    {
        // 进程中途崩溃时，等终态才拿 sessionId 就已经晚了 —— 续接指针会丢。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"system","session_id":"sess-early"}""",
                """{"type":"result","subtype":"success","session_id":"sess-early","result":"done"}"""
            ],
            onWrite: _ => []);

        var events = await DrainAsync(Adapter(), transport, Context());

        events.ShouldContain(e => e.Type == CliAgentEventType.Status && e.SessionId == "sess-early");
    }

    [Fact]
    public async Task Run_NormalizesRateLimitEventToStatusRatherThanLog()
    {
        // 实测存在的带外事件：配额窗口播报。它既不是内容也不是错误 ——
        // 归一化为 Status 才能让上层转成「接近限额」的用户提示。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"rate_limit_event","status":"approaching"}""",
                """{"type":"result","subtype":"success","result":"done"}"""
            ],
            onWrite: _ => []);

        var events = await DrainAsync(Adapter(), transport, Context());

        events.ShouldContain(e => e.Type == CliAgentEventType.Status && e.Status == "rate_limit");
    }

    [Fact]
    public async Task Run_DowngradesUnknownEventTypeToLogInsteadOfThrowing()
    {
        // CLI 会随版本新增事件类型。把未知形状当错误处理 = 每次上游升级都是一次故障。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"some_future_event","payload":{"x":1}}""",
                """not even json""",
                """{"type":"result","subtype":"success","result":"done"}"""
            ],
            onWrite: _ => []);

        var adapter = Adapter();
        var events = await DrainAsync(adapter, transport, Context());

        events.ShouldContain(e => e.Type == CliAgentEventType.Log && e.Content!.Contains("some_future_event"));
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Status.ShouldBe(CliRunStatus.Completed);
    }

    [Fact]
    public async Task Run_AnswersControlRequestOnTheSameStdin()
    {
        // 不应答的后果不是丢一条消息，而是子进程阻塞到它自己的内部超时，整个任务卡死。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"control_request","request_id":"req-1","request":{"subtype":"can_use_tool","tool_name":"Bash","input":{"command":"ls"}}}""",
                """{"type":"result","subtype":"success","result":"done"}"""
            ],
            onWrite: _ => []);

        await DrainAsync(Adapter(), transport, Context());

        transport.Written.ShouldContain(w => w.Contains("control_response") && w.Contains("\"behavior\":\"allow\""));
    }

    [Fact]
    public async Task GetResult_WithoutResultEvent_FailsInsteadOfReportingSuccess()
    {
        // 进程干净退出不等于协议跑完。没有 result 就没有终态。
        var transport = new FakeCliAgentTransport(
            ["""{"type":"assistant","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"partial"}]}}"""]);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context());

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Failed);
        result.FailureReason.ShouldBe(CliRunFailureReason.ProcessCrashed);
        // 失败时不给交付物：一段被截断的记录被当成"最终答案"回给用户，比一条明确的失败更糟。
        result.Output.ShouldBeNull();
    }

    [Fact]
    public async Task GetResult_WhenAssistantTurnInvokedATool_DoesNotUseItsNarrationAsOutput()
    {
        // 「我先看一下日志」不是交付物。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"assistant","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"Let me check the logs"},{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"cat log"}}]}}""",
                """{"type":"result","subtype":"success","result":""}"""
            ],
            onWrite: _ => []);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context());

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Output.ShouldNotBeNull();
        result.Output!.ShouldNotContain("Let me check the logs");
    }

    [Fact]
    public async Task GetResult_DetectsResumeRejectionFromStderrPhrase()
    {
        // 实测：result 事件的 subtype 是通用的 error_during_execution，不能用来判断；
        // 真正的区分信号在 stderr + 非零退出码。
        var transport = new FakeCliAgentTransport(
            ["""{"type":"result","subtype":"error_during_execution","is_error":true,"num_turns":0,"total_cost_usd":0,"session_id":"gone"}"""],
            stderrTail: "No conversation found with session ID: gone",
            onWrite: _ => []);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context("gone"));

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1, StderrTail = "No conversation found with session ID: gone" });
        result.ResumeRejected.ShouldBeTrue();
        result.FailureReason.ShouldBe(CliRunFailureReason.ResumeRejected);
        // 已知被拒的会话指针不能保存 —— 下一轮拿它续接必然再失败一次。
        result.SessionId.ShouldBeNull();
    }

    [Fact]
    public async Task GetResult_OnCancellation_DoesNotClaimTheResumeWasRejected()
    {
        // 取消是框架自己造成的。把它算成 resume 被拒，会白白丢掉一个仍然有效的会话指针。
        // 进程被中止后 stdout 随之关闭，所以这里用「已结束的流」建构。
        var transport = new FakeCliAgentTransport([]);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context("still-good"));

        var result = adapter.GetResult(new CliSessionOutcome { Cancelled = true, ExitCode = 1 });
        result.Status.ShouldBe(CliRunStatus.Cancelled);
        result.ResumeRejected.ShouldBeFalse();
    }

    [Fact]
    public async Task GetResult_OnWatchdogTimeout_DoesNotClaimTheResumeWasRejected()
    {
        var transport = new FakeCliAgentTransport([]);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context("still-good"));

        var result = adapter.GetResult(new CliSessionOutcome
        {
            WatchdogFailure = CliRunFailureReason.IdleTimeout,
            ExitCode = 1,
            StderrTail = "some noise"
        });

        result.Status.ShouldBe(CliRunStatus.TimedOut);
        result.ResumeRejected.ShouldBeFalse();
    }

    [Fact]
    public async Task GetResult_PrefersResultModelUsageOverPerFrameAccumulation()
    {
        // result 帧的 modelUsage 是本轮的权威汇总。两者相加会双计。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"assistant","message":{"role":"assistant","model":"m","usage":{"input_tokens":5,"output_tokens":3},"content":[{"type":"text","text":"hi"}]}}""",
                """{"type":"result","subtype":"success","result":"hi","modelUsage":{"m":{"inputTokens":5,"outputTokens":3,"cacheReadInputTokens":100,"cacheCreationInputTokens":50}}}"""
            ],
            onWrite: _ => []);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context());

        var usage = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Usage;
        usage["m"].InputTokens.ShouldBe(5);
        usage["m"].OutputTokens.ShouldBe(3);
        usage["m"].CacheReadTokens.ShouldBe(100);
    }
}
