namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// stream-json 上一个进程里的几轮（后台任务完成后 CLI 自己开的那一轮）、斜杠命令前的上下文、上下文压缩。
/// </summary>
/// <remarks>
/// 带 <c>Recording_</c> 前缀的用例回放 <c>Adapters/Recordings/</c> 里录下的真实 stdout（claude 2.1.317，stdin 保持打开，
/// 与适配器的用法相同；路径与个人配置已去掉）：
/// <list type="bullet">
/// <item><c>claude-background-task-follow-up.jsonl</c>：Bash <c>run_in_background</c> 起一条 20 秒的命令，第一轮答 STARTED；
/// 命令结束时 CLI 自己开一轮答 FINISHED。</item>
/// <item><c>claude-slash-command-with-context.jsonl</c>：先一帧 <c>shouldQuery: false</c> 的上下文，再 <c>/echo-args hello world</c>
/// （一个回显 $ARGUMENTS 的技能）：CLI 对上下文帧回一条空 result，命令的参数是干净的，技能读到了上下文里的词。</item>
/// <item><c>claude-compact.jsonl</c>：在一个续接的会话上发 <c>/compact</c>。</item>
/// <item><c>claude-resume-after-stopped-background-task.jsonl</c>（claude 2.1.318）：上一个进程在一轮结束、后台命令还在跑时被终止（取消），
/// 续接它、发一条新消息：CLI 先为那个停掉的任务发一条 <c>task_notification</c>（<c>stopped</c>）和一条空 result
/// （<c>origin.kind = task-notification</c>、<c>num_turns = 0</c>），然后才是这条消息的一轮。</item>
/// </list>
/// </remarks>
public class StreamJsonTurnsAndCommandsTests
{
    private const string ContextText = "Context for this turn: the secret word is PINEAPPLE.";

    private static CliAgentLaunchContext Context(
        string prompt = "hello", string? turnContext = null, TimeSpan? wait = null, string provider = "claude") => new()
    {
        Provider = CliBuiltInProviders.All[provider],
        ExecutablePath = "/nonexistent/claude",
        Prompt = prompt,
        TurnContext = turnContext,
        WorkingDirectory = "/tmp/does-not-need-to-exist",
        BackgroundTaskWait = wait ?? TimeSpan.FromMinutes(30)
    };

    private static string[] Recording(string name)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Adapters", "Recordings", name))
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

    private static StreamJsonAdapter NewAdapter() => new(NullLogger<StreamJsonAdapter>.Instance);

    private static async Task<List<CliAgentEvent>> RunAsync(
        ICliProtocolAdapter adapter, ICliAgentTransport transport, CliAgentLaunchContext context, CancellationToken cancellationToken = default)
    {
        var events = new List<CliAgentEvent>();
        await foreach (var evt in adapter.RunAsync(transport, context, cancellationToken))
        {
            events.Add(evt);
        }

        return events;
    }

    private static JsonElement Frame(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    private static string FrameText(JsonElement frame)
        => string.Concat(frame.GetProperty("message").GetProperty("content").EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "text").Select(b => b.GetProperty("text").GetString()));

    // ── 后台任务完成后的下一轮 ───────────────────────────────────────────────

    /// <summary>
    /// 录像：第一轮结束时 CLI 还有一个后台任务：产出 TurnCompleted（带那个任务）并接着读，任务完成的通知成一条状态，
    /// CLI 自己开的下一轮照常读完；最后一轮的 result 才结束读取，交付物是最后一轮的回答。
    /// </summary>
    [Fact]
    public async Task Recording_ATurnStartedByAFinishedBackgroundTask_IsReadAndEndsTheRun()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(Recording("claude-background-task-follow-up.jsonl"));

        var events = await RunAsync(adapter, transport, Context());

        var boundary = events.Where(e => e.Type == CliAgentEventType.TurnCompleted).ShouldHaveSingleItem();
        boundary.Content.ShouldBe("STARTED");
        var task = boundary.Turn.ShouldNotBeNull().BackgroundTasks.ShouldHaveSingleItem();
        task.Description.ShouldBe("Sleep then echo in background");
        task.Kind.ShouldBe("local_bash");

        var texts = events.Where(e => e.Type == CliAgentEventType.Text).Select(e => e.Content).ToList();
        texts.ShouldBe(["STARTED", "FINISHED BG_DONE"]);
        events.IndexOf(boundary).ShouldBeLessThan(events.FindIndex(e => e.Content == "FINISHED BG_DONE"));
        events.ShouldContain(e => e.Type == CliAgentEventType.Status && e.Status == "background_task_finished"
                                  && e.Content!.Contains("Sleep then echo in background"));
        transport.InputClosed.ShouldBeTrue();

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed);
        result.Output.ShouldBe("FINISHED BG_DONE");
        result.TurnUsage.ShouldNotBeNull().OutputTokens.ShouldBe(121 + 13, "the run's main-loop usage is the sum of its turns");
    }

    /// <summary>不等后台任务（<c>BackgroundTaskWait = 0</c>）：第一轮的 result 就结束读取，与此前的行为相同。</summary>
    [Fact]
    public async Task Recording_WithoutAWait_TheFirstResultEndsTheRun()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(Recording("claude-background-task-follow-up.jsonl"));

        var events = await RunAsync(adapter, transport, Context(wait: TimeSpan.Zero));

        events.ShouldNotContain(e => e.Type == CliAgentEventType.TurnCompleted);
        events.ShouldNotContain(e => e.Content == "FINISHED BG_DONE");
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Output.ShouldBe("STARTED");
    }

    /// <summary>
    /// 等后台任务等到了上限：安静地结束读取（不是错误），留一条 <c>background_tasks_stopped</c> 状态；进程被执行器终止，
    /// 退出码不算崩溃，第一轮的回答照常是交付物。
    /// </summary>
    [Fact]
    public async Task WhenTheWaitRunsOut_ReadingEndsQuietly_AndTheFirstAnswerIsDelivered()
    {
        var firstTurn = Recording("claude-background-task-follow-up.jsonl").TakeWhile(l => !l.Contains("\"subtype\":\"task_updated\"")).ToArray();
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(firstTurn, onWrite: _ => []); // stdout 不结束：CLI 还在等它的后台任务

        var events = await RunAsync(adapter, transport, Context(wait: TimeSpan.FromMilliseconds(200))).WaitAsync(TimeSpan.FromSeconds(10));

        events.ShouldContain(e => e.Type == CliAgentEventType.TurnCompleted);
        var stopped = events.Last();
        stopped.Type.ShouldBe(CliAgentEventType.Status);
        stopped.Status.ShouldBe("background_tasks_stopped");
        stopped.Content.ShouldBe("Sleep then echo in background");

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = -1 });
        result.Status.ShouldBe(CliRunStatus.Completed, result.Error);
        result.Output.ShouldBe("STARTED");
    }

    /// <summary>等下一轮时被取消（用户停下、宿主停机）是取消，不是「等到了上限」：异常照常交给执行器。</summary>
    [Fact]
    public async Task CancellingWhileWaiting_IsACancellation_NotTheEndOfTheWait()
    {
        var firstTurn = Recording("claude-background-task-follow-up.jsonl").TakeWhile(l => !l.Contains("\"subtype\":\"task_updated\"")).ToArray();
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(firstTurn, onWrite: _ => []);
        using var cancellation = new CancellationTokenSource();

        var events = new List<CliAgentEvent>();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var evt in adapter.RunAsync(transport, Context(), cancellation.Token))
            {
                events.Add(evt);
                if (evt.Type == CliAgentEventType.TurnCompleted)
                {
                    await cancellation.CancelAsync();
                }
            }
        });

        events.ShouldNotContain(e => e.Status == "background_tasks_stopped");
    }

    /// <summary>出错的一轮不等后台任务：错误就是这次运行的结局。</summary>
    [Fact]
    public async Task AnErrorResult_EndsTheRunEvenWithBackgroundTasks()
    {
        var lines = Recording("claude-background-task-follow-up.jsonl").TakeWhile(l => !l.Contains("\"type\":\"result\"")).ToList();
        lines.Add("""{"type":"result","subtype":"error_during_execution","is_error":true,"result":"boom","session_id":"s","num_turns":1}""");
        var adapter = NewAdapter();

        var events = await RunAsync(adapter, new FakeCliAgentTransport(lines, onWrite: _ => []), Context()).WaitAsync(TimeSpan.FromSeconds(10));

        events.ShouldNotContain(e => e.Type == CliAgentEventType.TurnCompleted);
        adapter.GetResult(new CliSessionOutcome { ExitCode = 1 }).Status.ShouldBe(CliRunStatus.Failed);
    }

    // ── 续接一个后台任务被停掉的会话 ─────────────────────────────────────────

    private static readonly string[] StoppedTaskNotice =
    [
        """{"type":"system","subtype":"task_notification","task_id":"old_task","status":"stopped","summary":"Background shell command didn't finish before the previous session ended","session_id":"s"}""",
        """{"type":"system","subtype":"init","session_id":"s"}""",
        """{"is_error":false,"num_turns":0,"session_id":"s","origin":{"kind":"task-notification"},"subtype":"success","result":"","type":"result"}"""
    ];

    /// <summary>
    /// 录像：续接的会话先为上一个进程里停掉的后台任务回一条空 result。它不是这次运行的一轮：吞掉、接着读，
    /// 这条消息的一轮读完才结束。此前它结束了读取，执行器在 CLI 还在回答时终止进程，运行以「exited with code 143」失败、回答丢了。
    /// </summary>
    [Fact]
    public async Task Recording_TheResultForAStoppedTaskOfThePreviousProcess_DoesNotEndTheRun()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(Recording("claude-resume-after-stopped-background-task.jsonl"));

        var events = await RunAsync(adapter, transport, Context() with { ResumeSessionId = "5ce743f9-d60c-47c3-8419-41e738fe4d06" });

        events.ShouldNotContain(e => e.Type == CliAgentEventType.TurnCompleted);
        events.ShouldContain(e => e.Type == CliAgentEventType.ToolUse);
        events.Where(e => e.Type == CliAgentEventType.Text).Select(e => e.Content).ShouldBe(["42"]);
        transport.InputClosed.ShouldBeTrue();

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed);
        result.Output.ShouldBe("42");
    }

    /// <summary>吞掉那条空 result 之后流断了（CLI 崩了）：照常是失败，不因为见过一条 result 就算完成。</summary>
    [Fact]
    public async Task AStreamThatEndsAfterTheStoppedTaskResult_IsStillACrash()
    {
        var adapter = NewAdapter();
        var lines = Recording("claude-resume-after-stopped-background-task.jsonl")
            .Take(5).ToArray();

        await RunAsync(adapter, new FakeCliAgentTransport(lines), Context()).WaitAsync(TimeSpan.FromSeconds(10));

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1 });
        result.Status.ShouldBe(CliRunStatus.Failed);
        result.FailureReason.ShouldBe(CliRunFailureReason.ProcessCrashed);
    }

    /// <summary>
    /// 这次运行自己起的后台任务完成后，CLI 开的那一轮的 result 也带 <c>origin.kind = task-notification</c>：它照常结束运行，
    /// 只有为别人的任务（这次运行没起过的）回的那条才吞掉。
    /// </summary>
    [Fact]
    public async Task AResultForATaskOfThisRun_IsNotSwallowed()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(
        [
            """{"type":"system","subtype":"init","session_id":"s"}""",
            """{"type":"system","subtype":"background_tasks_changed","tasks":[{"task_id":"mine","task_type":"local_bash","description":"build"}],"session_id":"s"}""",
            """{"type":"assistant","message":{"model":"m","id":"msg_1","role":"assistant","content":[{"type":"text","text":"started"}]},"session_id":"s"}""",
            """{"type":"result","subtype":"success","result":"started","session_id":"s","num_turns":1}""",
            """{"type":"system","subtype":"task_notification","task_id":"mine","status":"completed","summary":"done","session_id":"s"}""",
            """{"type":"system","subtype":"background_tasks_changed","tasks":[],"session_id":"s"}""",
            """{"type":"system","subtype":"init","session_id":"s"}""",
            """{"type":"assistant","message":{"model":"m","id":"msg_2","role":"assistant","content":[{"type":"text","text":"finished"}]},"session_id":"s"}""",
            """{"type":"result","subtype":"success","result":"finished","session_id":"s","num_turns":1,"origin":{"kind":"task-notification","producer":"session-task"}}""",
            """{"type":"assistant","message":{"model":"m","id":"msg_3","role":"assistant","content":[{"type":"text","text":"never read"}]},"session_id":"s"}"""
        ]);

        var events = await RunAsync(adapter, transport, Context());

        events.ShouldNotContain(e => e.Content == "never read");
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Output.ShouldBe("finished");
    }

    /// <summary>
    /// 一轮开始之后才到的、不认识的任务通知（前台工具的那类）不改变什么：这一轮自己的 result 照常结束读取。
    /// </summary>
    [Fact]
    public async Task ANotificationForAnUnknownTaskDuringTheTurn_LeavesTheTurnsResultTerminal()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(
        [
            """{"type":"system","subtype":"init","session_id":"s"}""",
            """{"type":"assistant","message":{"model":"m","id":"msg_1","role":"assistant","content":[{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"ls"}}]},"session_id":"s"}""",
            """{"type":"system","subtype":"task_notification","task_id":"foreground","status":"completed","summary":"ls","session_id":"s"}""",
            """{"type":"user","message":{"role":"user","content":[{"tool_use_id":"t1","type":"tool_result","content":"a.txt"}]},"session_id":"s"}""",
            """{"type":"assistant","message":{"model":"m","id":"msg_2","role":"assistant","content":[{"type":"text","text":"a.txt"}]},"session_id":"s"}""",
            """{"type":"result","subtype":"success","result":"a.txt","session_id":"s","num_turns":2,"origin":{"kind":"task-notification"}}""",
            """{"type":"assistant","message":{"model":"m","id":"msg_3","role":"assistant","content":[{"type":"text","text":"never read"}]},"session_id":"s"}"""
        ]);

        var events = await RunAsync(adapter, transport, Context());

        events.ShouldNotContain(e => e.Content == "never read");
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Output.ShouldBe("a.txt");
    }

    /// <summary>上一个进程停掉了不止一个后台任务：每条通知各回一条空 result，都吞掉。</summary>
    [Fact]
    public async Task SeveralStoppedTasks_EachEmptyResultIsSwallowed()
    {
        var adapter = NewAdapter();
        var second = StoppedTaskNotice[0].Replace("old_task", "other_task", StringComparison.Ordinal);
        var transport = new FakeCliAgentTransport(
        [
            StoppedTaskNotice[0], second, StoppedTaskNotice[1], StoppedTaskNotice[2], StoppedTaskNotice[2],
            """{"type":"system","subtype":"init","session_id":"s"}""",
            """{"type":"assistant","message":{"model":"m","id":"msg_1","role":"assistant","content":[{"type":"text","text":"answer"}]},"session_id":"s"}""",
            """{"type":"result","subtype":"success","result":"answer","session_id":"s","num_turns":1}"""
        ]);

        await RunAsync(adapter, transport, Context());

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed);
        result.Output.ShouldBe("answer");
    }

    // ── 斜杠命令与这一轮的上下文 ────────────────────────────────────────────

    /// <summary>
    /// 录像：这一轮是斜杠命令、带上下文：先写一帧只含上下文的 <c>shouldQuery: false</c> 用户帧，再写命令本身（原样，不带上下文）。
    /// CLI 对上下文帧回的空 result 被吞掉、不结束读取；技能拿到的参数是 "hello world"，读到了上下文里的词。
    /// </summary>
    [Fact]
    public async Task Recording_ASlashCommand_GetsItsContextInAFrameOfItsOwn_AndCleanArguments()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(Recording("claude-slash-command-with-context.jsonl"));

        var events = await RunAsync(adapter, transport, Context(prompt: "/echo-args hello world", turnContext: ContextText));

        transport.Written.Count.ShouldBe(2);
        var contextFrame = Frame(transport.Written[0]);
        contextFrame.GetProperty("shouldQuery").GetBoolean().ShouldBeFalse();
        FrameText(contextFrame).ShouldBe(ContextText);
        var commandFrame = Frame(transport.Written[1]);
        commandFrame.TryGetProperty("shouldQuery", out _).ShouldBeFalse();
        FrameText(commandFrame).ShouldBe("/echo-args hello world");

        events.ShouldNotContain(e => e.Type == CliAgentEventType.TurnCompleted);
        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed);
        result.Output.ShouldBe("ARGS=[hello world]\nSECRET=PINEAPPLE");
    }

    /// <summary>agent 说明（内联的 brief）同样不能跟在命令前面：它和上下文一起进上下文帧。</summary>
    [Fact]
    public async Task ASlashCommand_TheInlineInstructionsGoInTheContextFrameToo()
    {
        var transport = new FakeCliAgentTransport(Recording("claude-slash-command-with-context.jsonl"));

        await RunAsync(NewAdapter(), transport, Context(prompt: "/review src", turnContext: ContextText) with { InlineSystemPrompt = "Answer briefly." });

        FrameText(Frame(transport.Written[0])).ShouldBe($"Answer briefly.\n\n---\n\n{ContextText}");
        FrameText(Frame(transport.Written[1])).ShouldBe("/review src");
    }

    /// <summary>附件也放在上下文帧里：命令那一帧必须只有一个文本块，否则 CLI 认不出命令。</summary>
    [Fact]
    public async Task ASlashCommand_WithAnAttachment_KeepsTheCommandFrameToOneTextBlock()
    {
        var transport = new FakeCliAgentTransport(Recording("claude-slash-command-with-context.jsonl"));
        var context = Context(prompt: "/describe") with { Attachments = [new CliPromptAttachment("image/png", "a.png", [1, 2, 3])] };

        await RunAsync(NewAdapter(), transport, context);

        var contextFrame = Frame(transport.Written[0]);
        contextFrame.GetProperty("shouldQuery").GetBoolean().ShouldBeFalse();
        contextFrame.GetProperty("message").GetProperty("content")[0].GetProperty("type").GetString().ShouldBe("image");
        Frame(transport.Written[1]).GetProperty("message").GetProperty("content").GetArrayLength().ShouldBe(1);
    }

    /// <summary>没有东西要随命令带上时照常一帧，不发上下文帧（也就没有要吞的空 result）。</summary>
    [Fact]
    public async Task ASlashCommandWithNothingToCarry_IsSentAsOneFrame()
    {
        var transport = new FakeCliAgentTransport(["""{"type":"result","subtype":"success","result":"ok","session_id":"s","num_turns":1}"""]);
        var adapter = NewAdapter();

        await RunAsync(adapter, transport, Context(prompt: "/context"));

        var frame = Frame(transport.Written.ShouldHaveSingleItem());
        frame.TryGetProperty("shouldQuery", out _).ShouldBeFalse();
        FrameText(frame).ShouldBe("/context");
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Output.ShouldBe("ok");
    }

    /// <summary>普通消息照旧：上下文以 <c>---</c> 分隔追加在提示尾部，一帧。</summary>
    [Fact]
    public async Task AnOrdinaryMessage_CarriesItsContextAtTheEnd_InOneFrame()
    {
        var transport = new FakeCliAgentTransport(["""{"type":"result","subtype":"success","result":"ok","session_id":"s","num_turns":1}"""]);

        await RunAsync(NewAdapter(), transport, Context(prompt: "what is the word?", turnContext: ContextText));

        FrameText(Frame(transport.Written.ShouldHaveSingleItem())).ShouldBe($"what is the word?\n\n---\n\n{ContextText}");
    }

    /// <summary>provider 不接受「只进会话」的用户帧：只能照常追加（命令会把上下文当参数收下，文档写明了这一点）。</summary>
    [Fact]
    public async Task OnAProviderWithoutContextFrames_ASlashCommandsContextIsAppended()
    {
        var transport = new FakeCliAgentTransport(["""{"type":"result","subtype":"success","result":"ok","session_id":"s","num_turns":1}"""]);

        await RunAsync(NewAdapter(), transport, Context(prompt: "/echo-args hi", turnContext: ContextText, provider: "qwen"));

        FrameText(Frame(transport.Written.ShouldHaveSingleItem())).ShouldBe($"/echo-args hi\n\n---\n\n{ContextText}");
    }

    [Theory]
    [InlineData("/compact", true)]
    [InlineData("/echo-args hello world", true)]
    [InlineData("  /plugin:skill-name args", true)]
    [InlineData("/review\nsrc", true)]
    [InlineData("hello /compact", false)]
    [InlineData("/ not a command", false)]
    [InlineData("//comment", false)]
    [InlineData("", false)]
    public void IsSlashCommand_MatchesWhatTheCliExpands(string prompt, bool expected)
        => StreamJsonAdapter.IsSlashCommand(prompt).ShouldBe(expected);

    // ── 上下文压缩 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 录像：<c>/compact</c> 产出 Compacted（触发方式、压缩前后的 token 数），这一轮没有回答：交付物为 null（不说「没有最终回答」），
    /// 结果带 Compaction 与本地命令名，上下文占用是压缩后的大小。
    /// </summary>
    [Fact]
    public async Task Recording_Compact_IsATypedOutcome_NotAMissingAnswer()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(Recording("claude-compact.jsonl"));

        var events = await RunAsync(adapter, transport, Context(prompt: "/compact"));

        var compacted = events.Where(e => e.Type == CliAgentEventType.Compacted).ShouldHaveSingleItem();
        var compaction = compacted.Compaction.ShouldNotBeNull();
        compaction.Trigger.ShouldBe("manual");
        compaction.PreTokens.ShouldNotBeNull().ShouldBeGreaterThan(compaction.PostTokens!.Value);

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed);
        result.Output.ShouldBeNull();
        result.Compaction.ShouldBe(compaction);
        result.LocalCommand.ShouldBe("compact");
        result.Context.ShouldNotBeNull().Tokens.ShouldBe(compaction.PostTokens.Value);
    }

    /// <summary>压缩之后还有回答（上下文将满时的自动压缩）：交付物照常是回答，Compaction 只是记一笔。</summary>
    [Fact]
    public async Task AnAutomaticCompactionInsideATurn_LeavesTheAnswerAsTheDeliverable()
    {
        var adapter = NewAdapter();
        var transport = new FakeCliAgentTransport(
        [
            """{"type":"system","subtype":"compact_boundary","session_id":"s","compact_metadata":{"trigger":"auto","pre_tokens":190000,"post_tokens":12000}}""",
            """{"type":"assistant","message":{"model":"m","id":"msg_1","role":"assistant","content":[{"type":"text","text":"Here is the answer."}]}}""",
            """{"type":"result","subtype":"success","result":"Here is the answer.","session_id":"s","num_turns":1}"""
        ]);

        var events = await RunAsync(adapter, transport, Context());

        events.ShouldContain(e => e.Type == CliAgentEventType.Compacted && e.Compaction!.Trigger == "auto");
        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Output.ShouldBe("Here is the answer.");
        result.LocalCommand.ShouldBeNull();
    }
}
