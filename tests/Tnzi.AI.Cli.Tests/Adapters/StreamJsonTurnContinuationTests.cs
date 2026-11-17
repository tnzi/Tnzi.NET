namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// stream-json 进程在等后台任务时交给同线程的下一条运行（<see cref="ICliTurnContinuation"/>，文档 §6.18）：
/// 等着的那条到此结算，下一条的用户帧写进同一个 stdin，接着读同一条事件流。
/// </summary>
/// <remarks>
/// 帧按 <c>claude-background-task-follow-up.jsonl</c> 录像的形状手写（session、后台任务、result 的 <c>origin</c>），
/// 写进 stdin 的用户帧由假 transport 应答成下一轮。
/// </remarks>
public class StreamJsonTurnContinuationTests
{
    private const string Session = "s-1";

    private static CliAgentLaunchContext Context(string prompt, TimeSpan? wait = null) => new()
    {
        Provider = CliBuiltInProviders.All["claude"],
        ExecutablePath = "/nonexistent/claude",
        Prompt = prompt,
        WorkingDirectory = "/tmp/does-not-need-to-exist",
        BackgroundTaskWait = wait ?? TimeSpan.FromMinutes(30)
    };

    private static CliAgentLaunchContext Context(string prompt, string? effort, bool switchable = true)
        => Context(prompt) with
        {
            ThinkingLevel = effort,
            Provider = CliBuiltInProviders.All["claude"] with { ChangesEffortInSession = switchable }
        };

    private static string Init() => $$"""{"type":"system","subtype":"init","session_id":"{{Session}}"}""";

    private static string Tasks(params string[] ids)
        => JsonSerializer.Serialize(new
        {
            type = "system", subtype = "background_tasks_changed", session_id = Session,
            tasks = ids.Select(id => new { task_id = id, task_type = "local_bash", description = $"server {id}" })
        });

    private static string Text(string id, string text, int outputTokens)
        => JsonSerializer.Serialize(new
        {
            type = "assistant", uuid = $"entry-{id}", session_id = Session,
            message = new
            {
                model = "m", id, role = "assistant",
                content = new[] { new { type = "text", text } },
                usage = new { input_tokens = 1, output_tokens = outputTokens }
            }
        });

    private static string Result(string text, int totalOutput, int turnOutput, string? origin = null, int queued = 0)
    {
        var frame = new JsonObject
        {
            ["type"] = "result", ["subtype"] = "success", ["result"] = text, ["session_id"] = Session, ["num_turns"] = 1,
            ["queued_turn_count"] = queued,
            ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = turnOutput },
            ["modelUsage"] = new JsonObject { ["m"] = new JsonObject { ["inputTokens"] = 1, ["outputTokens"] = totalOutput, ["costUSD"] = 0.01 } }
        };
        if (origin is not null)
        {
            frame["origin"] = new JsonObject { ["kind"] = origin };
        }

        return frame.ToJsonString();
    }

    private static string[] FirstTurnWithServer() =>
    [
        Init(),
        Tasks("srv"),
        Text("msg_1", "The server is running.", 10),
        Result("The server is running.", totalOutput: 10, turnOutput: 10)
    ];

    private static StreamJsonAdapter NewAdapter() => new(NullLogger<StreamJsonAdapter>.Instance);

    /// <summary>读事件流的后台循环：测试按事件推进，像执行器那样在两轮之间交接。</summary>
    private sealed class Reader
    {
        private readonly Channel<CliAgentEvent> _events = Channel.CreateUnbounded<CliAgentEvent>();

        public Reader(ICliProtocolAdapter adapter, ICliAgentTransport transport, CliAgentLaunchContext context, CancellationToken cancellationToken = default)
        {
            Completion = Task.Run(async () =>
            {
                try
                {
                    await foreach (var evt in adapter.RunAsync(transport, context, cancellationToken))
                    {
                        await _events.Writer.WriteAsync(evt, CancellationToken.None);
                    }
                }
                finally
                {
                    _events.Writer.TryComplete();
                }
            });
        }

        public Task Completion { get; }

        public List<CliAgentEvent> Seen { get; } = [];

        public async Task<CliAgentEvent> UntilAsync(Func<CliAgentEvent, bool> match)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (await _events.Reader.WaitToReadAsync(timeout.Token))
            {
                while (_events.Reader.TryRead(out var evt))
                {
                    Seen.Add(evt);
                    if (match(evt))
                    {
                        return evt;
                    }
                }
            }

            throw new InvalidOperationException("The event stream ended before the expected event.");
        }

        public async Task<List<CliAgentEvent>> RestAsync()
        {
            await Completion.WaitAsync(TimeSpan.FromSeconds(10));
            var rest = new List<CliAgentEvent>();
            while (_events.Reader.TryRead(out var evt))
            {
                rest.Add(evt);
            }

            Seen.AddRange(rest);
            return rest;
        }
    }

    /// <summary>
    /// 第一轮起了一个不会结束的后台任务（开发服务器）并答完：交接时等着的那条结算到这里（第一轮的回答与用量），
    /// 下一条运行的提示写进同一个 stdin，它那一轮从同一条事件流读出；结算从零开始，服务器还在时它再等一次（又一个 TurnCompleted）。
    /// </summary>
    [Fact]
    public async Task WhileWaiting_TheNextTurnIsWrittenToTheSameProcess_AndCountedOnItsOwn()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(FirstTurnWithServer(), onWrite: line => line.Contains("what is 17*3")
            ? [Init(), Text("msg_2", "51", 3), Result("51", totalOutput: 13, turnOutput: 3)]
            : []);
        var reader = new Reader(adapter, transport, Context("start the dev server"));

        await reader.UntilAsync(e => e.Type == CliAgentEventType.TurnCompleted);
        adapter.IsAwaitingFollowUp.ShouldBeTrue();

        var first = adapter.CompleteRunSegment(new CliSessionOutcome { Elapsed = TimeSpan.FromSeconds(1) });
        first.Status.ShouldBe(CliRunStatus.Completed, first.Error);
        first.Output.ShouldBe("The server is running.");
        first.SessionId.ShouldBe(Session);
        first.SessionEntryId.ShouldBe("entry-msg_1");
        first.TurnUsage.ShouldNotBeNull().OutputTokens.ShouldBe(10);

        (await adapter.ContinueAsync(Context("what is 17*3"), CancellationToken.None)).ShouldBeTrue();
        adapter.IsAwaitingFollowUp.ShouldBeFalse();
        transport.Written.Count.ShouldBe(2, "the first prompt and the next run's prompt, on the same stdin");

        // 服务器还在跑：下一条运行的这一轮结束时它也在等后台任务
        var boundary = await reader.UntilAsync(e => e.Type == CliAgentEventType.TurnCompleted);
        boundary.Content.ShouldBe("51");
        reader.Seen.ShouldContain(e => e.Type == CliAgentEventType.Text && e.Content == "51");
        transport.InputClosed.ShouldBeFalse("the process stays open for the server");

        var second = adapter.CompleteRunSegment(new CliSessionOutcome { Elapsed = TimeSpan.FromSeconds(1) });
        second.Output.ShouldBe("51");
        second.TurnUsage.ShouldNotBeNull().OutputTokens.ShouldBe(3, "the next run counts only its own turns");
        second.Usage.ShouldContainKey("m");
        second.Usage["m"].OutputTokens.ShouldBe(13, "the session totals the CLI reports; the executor subtracts the previous run's");
        second.SessionEntryId.ShouldBe("entry-msg_2");
    }

    /// <summary>
    /// 接手的这一轮选了另一档强度：claude 能在会话里换（实测 2.1.308 的 <c>apply_flag_settings</c>），先写换档的控制请求、再写用户帧，
    /// 进程（连同后台任务）不用另起。再接一轮、档位不变时不再写。
    /// </summary>
    [Fact]
    public async Task ADifferentEffort_IsSwitchedInTheSession_BeforeTheNextTurnsPrompt()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(FirstTurnWithServer(), onWrite: line => line.Contains("\"type\":\"user\"") && !line.Contains("start the dev server")
            ? [Text($"msg_{Guid.NewGuid():N}", "ok", 1), Result("ok", totalOutput: 11, turnOutput: 1)]
            : []);
        var reader = new Reader(adapter, transport, Context("start the dev server", "high"));
        await reader.UntilAsync(e => e.Type == CliAgentEventType.TurnCompleted);
        adapter.CompleteRunSegment(new CliSessionOutcome());

        (await adapter.ContinueAsync(Context("think less", "low"), CancellationToken.None)).ShouldBeTrue();

        transport.Written.Count.ShouldBe(3);
        var control = JsonNode.Parse(transport.Written[1])!;
        control["type"]!.GetValue<string>().ShouldBe("control_request");
        control["request"]!["subtype"]!.GetValue<string>().ShouldBe("apply_flag_settings");
        control["request"]!["settings"]!["effortLevel"]!.GetValue<string>().ShouldBe("low");
        transport.Written[2].ShouldContain("think less");

        await reader.UntilAsync(e => e.Type == CliAgentEventType.TurnCompleted);
        adapter.CompleteRunSegment(new CliSessionOutcome());
        (await adapter.ContinueAsync(Context("again", "low"), CancellationToken.None)).ShouldBeTrue();
        transport.Written.Count.ShouldBe(4, "the session already runs at low: only the prompt");
    }

    /// <summary>不能在会话里换强度的 provider 不写换档请求（执行器在启动参数不同时就不会把进程交过来）。</summary>
    [Fact]
    public async Task AProviderThatCannotSwitch_GetsNoEffortRequest()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(FirstTurnWithServer(), onWrite: _ => []);
        var reader = new Reader(adapter, transport, Context("start the dev server", "high", switchable: false));
        await reader.UntilAsync(e => e.Type == CliAgentEventType.TurnCompleted);
        adapter.CompleteRunSegment(new CliSessionOutcome());

        (await adapter.ContinueAsync(Context("next", "low", switchable: false), CancellationToken.None)).ShouldBeTrue();

        transport.Written.ShouldNotContain(line => line.Contains("apply_flag_settings"));
    }

    /// <summary>后台任务在下一条运行那一轮之前结束（没有任务、没有排队的轮次）：那一轮的 result 结束读取，stdin 关闭。</summary>
    [Fact]
    public async Task WhenNoTaskIsLeftAfterTheContinuedTurn_ItsResultEndsTheReading()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(FirstTurnWithServer(), onWrite: line => line.Contains("stop the server")
            ? [Init(), Tasks(), Text("msg_2", "Stopped.", 2), Result("Stopped.", totalOutput: 12, turnOutput: 2)]
            : []);
        var reader = new Reader(adapter, transport, Context("start the dev server"));

        await reader.UntilAsync(e => e.Type == CliAgentEventType.TurnCompleted);
        adapter.CompleteRunSegment(new CliSessionOutcome());
        (await adapter.ContinueAsync(Context("stop the server"), CancellationToken.None)).ShouldBeTrue();

        var rest = await reader.RestAsync();
        rest.ShouldNotContain(e => e.Type == CliAgentEventType.TurnCompleted);
        transport.InputClosed.ShouldBeTrue();
        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed, result.Error);
        result.Output.ShouldBe("Stopped.");
    }

    /// <summary>
    /// 交接与后台任务完成撞在一起：CLI 先开了任务通知的那一轮（result 带 <c>origin.kind = task-notification</c>，没有任务、没有排队），
    /// 它之后才是写进去的那一轮。前者不结束读取（而是一轮的边界），后者照常读到。
    /// </summary>
    [Fact]
    public async Task ATaskNotificationTurnBeforeTheSubmittedOne_DoesNotEndTheReading()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(FirstTurnWithServer(), onWrite: line => line.Contains("next question")
            ?
            [
                """{"type":"system","subtype":"task_notification","task_id":"srv","status":"completed","summary":"server exited","session_id":"s-1"}""",
                Tasks(),
                Init(),
                Text("msg_2", "The server exited.", 4),
                Result("The server exited.", totalOutput: 14, turnOutput: 4, origin: "task-notification"),
                Init(),
                Text("msg_3", "Here is the answer.", 5),
                Result("Here is the answer.", totalOutput: 19, turnOutput: 5)
            ]
            : []);
        var reader = new Reader(adapter, transport, Context("start the dev server"));

        await reader.UntilAsync(e => e.Type == CliAgentEventType.TurnCompleted);
        adapter.CompleteRunSegment(new CliSessionOutcome());
        (await adapter.ContinueAsync(Context("next question"), CancellationToken.None)).ShouldBeTrue();

        var boundary = await reader.UntilAsync(e => e.Type == CliAgentEventType.TurnCompleted);
        boundary.Content.ShouldBe("The server exited.");
        var rest = await reader.RestAsync();
        rest.ShouldContain(e => e.Type == CliAgentEventType.Text && e.Content == "Here is the answer.");
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Output.ShouldBe("Here is the answer.");
    }

    /// <summary>没有在等（一轮还在进行、或者读取已经结束）时不交接：什么都不写，返回 false。</summary>
    [Fact]
    public async Task OutsideTheWait_NothingIsWritten()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport([Init(), Text("msg_1", "Done.", 1), Result("Done.", 1, 1)]);
        var reader = new Reader(adapter, transport, Context("hello"));
        await reader.RestAsync();

        adapter.IsAwaitingFollowUp.ShouldBeFalse();
        (await adapter.ContinueAsync(Context("next"), CancellationToken.None)).ShouldBeFalse();
        transport.Written.Count.ShouldBe(1);
    }

    /// <summary>等待已经到了上限（读取结束、执行器要终止进程）：不交接。</summary>
    [Fact]
    public async Task AfterTheWaitRanOut_NothingIsWritten()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(FirstTurnWithServer(), onWrite: _ => []);
        var reader = new Reader(adapter, transport, Context("start the dev server", wait: TimeSpan.FromMilliseconds(100)));

        var rest = await reader.RestAsync();
        rest.Last().Status.ShouldBe("background_tasks_stopped");
        (await adapter.ContinueAsync(Context("next"), CancellationToken.None)).ShouldBeFalse();
    }
}
