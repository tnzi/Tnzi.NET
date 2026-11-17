using Tnzi.AI.Engine;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 子 agent 在线程里的样子：派出它的那次调用带着它的状态与步骤，它的调用归在它下面，它写的字不进回复；
/// 后台子 agent 后来的进展与结局写回派出它的那条回复；运行结束时还在跑的子 agent 记为停止。流式时看到的与之相同。
/// </summary>
/// <remarks>
/// 事件取自真实适配器回放录像（<c>Adapters/Recordings/claude-subagent-*.jsonl</c>）的输出，再交给执行器与门面：
/// 从 CLI 的一帧到线程里的一条回复，整条链用的是同一份数据。
/// </remarks>
public class CliSubAgentReplyTests : CliChatTurnTestBase
{
    private const string OuterCall = "toolu_01CrxJifHDgGAuEFdoAGzgyQ";
    private const string InnerCall = "toolu_01L46M73LNMPjyxPxzhNmhrK";
    private const string BackgroundCall = "toolu_01XJsahMZpxvALW8tHmnKeC3";

    private static async Task<(List<CliAgentEvent> Events, string? Output)> RecordedAsync(string name)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Adapters", "Recordings", name))
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var context = new CliAgentLaunchContext
        {
            Provider = CliBuiltInProviders.All["claude"],
            ExecutablePath = "/nonexistent/claude",
            Prompt = "hello",
            WorkingDirectory = "/tmp/does-not-need-to-exist",
            BackgroundTaskWait = TimeSpan.FromMinutes(30)
        };

        var events = new List<CliAgentEvent>();
        await foreach (var evt in adapter.RunAsync(new FakeCliAgentTransport(lines), context, CancellationToken.None))
        {
            events.Add(evt);
        }

        return (events, adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Output);
    }

    private async Task<List<AgentThreadMessage>> RepliesAsync(Guid threadId)
        => await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .Where(m => m.ThreadId == threadId && m.Role == MessageRole.Assistant)
            .OrderBy(m => m.Order).ToListAsync();

    private static List<ToolCallDetail> CallsOf(AgentThreadMessage reply)
        => JsonSerializer.Deserialize<List<ToolCallDetail>>(reply.ToolCalls!, TnziJsonDefaults.Options)!;

    [Fact]
    public async Task AForegroundSubAgent_IsACardOnTheReply_WithItsCallsAndTheNestedOneUnderIt()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("read both files");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var (events, output) = await RecordedAsync("claude-subagent-foreground-nested.jsonl");

        await CreateExecutor(new ScriptedAdapter(events, output: output)).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var reply = (await RepliesAsync(threadId)).ShouldHaveSingleItem();
        reply.Content.ShouldStartWith("alpha.txt 内容为");
        reply.Content.ShouldNotContain("两个文件的内容如下", Case.Sensitive, "the sub-agent's report is not the reply");

        var calls = CallsOf(reply);
        calls.Select(c => (c.Name, c.ParentId)).ShouldBe([("Agent", null), ("Read", OuterCall), ("Agent", OuterCall)]);

        var outer = calls[0].Task.ShouldNotBeNull();
        outer.Status.ShouldBe(CliTaskStatuses.Completed);
        outer.AgentType.ShouldBe("general-purpose");
        outer.Description.ShouldBe("Read alpha and beta files");
        outer.Result.ShouldNotBeNull().ShouldContain("This file is beta.");
        outer.ToolUseCount.ShouldBe(2);
        outer.DurationMs.ShouldBe(10697);
        outer.StartedAt.ShouldNotBeNull();
        outer.Steps.Select(s => s.ToolCallId).ShouldBe(["toolu_016ZPDU5RQdgAMtNT4QYwaym", InnerCall]);

        calls[2].Task.ShouldNotBeNull().Status.ShouldBe(CliTaskStatuses.Completed);
        calls[2].Task!.Description.ShouldBe("Read beta.txt and report content");

        var rows = await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters().Where(m => m.RunId == run.Id).ToListAsync();
        rows.Single(m => m.Type == CliAgentEventType.ToolUse && m.Tool == "Read").ParentCallId.ShouldBe(OuterCall);
        var started = rows.First(m => m.Type == CliAgentEventType.Task && m.CallId == InnerCall);
        started.ParentCallId.ShouldBe(OuterCall);
        CliRunMessageConverter.ToEvent(started).Task.ShouldNotBeNull().Description.ShouldBe("Read beta.txt and report content");
    }

    [Fact]
    public async Task ABackgroundSubAgent_FinishingInALaterTurn_UpdatesTheReplyThatDispatchedIt()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("read alpha in the background");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var (events, output) = await RecordedAsync("claude-subagent-background.jsonl");

        await CreateExecutor(new ScriptedAdapter(events, output: output)).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var replies = await RepliesAsync(threadId);
        replies.Select(m => m.Content).ShouldBe(["Started. 后台任务已启动，正在读取 alpha.txt，完成后我会回报结果。", "alpha.txt 的内容是：`This file is alpha.`"]);
        replies[1].ToolCalls.ShouldBeNull("the sub-agent belongs to the answer that dispatched it");

        var calls = CallsOf(replies[0]);
        calls.Select(c => (c.Name, c.ParentId)).ShouldBe([("Agent", null), ("Read", BackgroundCall)]);
        var subAgent = calls[0].Task.ShouldNotBeNull();
        subAgent.Background.ShouldBeTrue();
        subAgent.Status.ShouldBe(CliTaskStatuses.Completed, "rewritten when the later turn was stored");
        subAgent.Result.ShouldNotBeNull().ShouldContain("This file is alpha.");
        subAgent.Steps.Select(s => s.Type).ShouldBe([ToolTaskStep.ToolType, ToolTaskStep.TextType]);
        subAgent.Steps[1].Text.ShouldNotBeNull().ShouldContain("alpha.txt 的内容只有一行");
    }

    [Fact]
    public async Task ASubAgentStillRunningWhenTheRunEnds_IsStoredAsStopped()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("start a long survey");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter(
        [
            new CliAgentEvent { Type = CliAgentEventType.ToolUse, CallId = "agent-1", Tool = "Agent" },
            new CliAgentEvent { Type = CliAgentEventType.Task, CallId = "agent-1", Task = new CliTask { Background = true, Description = "Survey" } },
            new CliAgentEvent { Type = CliAgentEventType.ToolResult, CallId = "agent-1", Status = "completed", Output = "launched" },
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Started." },
            new CliAgentEvent
            {
                Type = CliAgentEventType.TurnCompleted, Content = "Started.",
                Turn = new CliTurnBoundary { BackgroundTasks = [new CliBackgroundTask("t1", "Survey", "local_agent")] }
            },
            new CliAgentEvent { Type = CliAgentEventType.Status, Status = "background_tasks_stopped", Content = "Survey" }
        ], output: "Started.");

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var reply = (await RepliesAsync(threadId)).ShouldHaveSingleItem();
        CallsOf(reply).ShouldHaveSingleItem().Task!.Status.ShouldBe(CliTaskStatuses.Stopped);
    }

    [Fact]
    public async Task ThroughTheFacade_ASubAgentsWork_StreamsAsUpdatesOfItsCard_NotAsReplyText()
    {
        await SeedBindingAsync();
        var (events, output) = await RecordedAsync("claude-subagent-background.jsonl");
        var withThinking = events.Prepend(new CliAgentEvent { Type = CliAgentEventType.Thinking, Content = "sub thoughts", ParentCallId = BackgroundCall }).ToList();

        var chunks = await StreamThroughAsync(CreateFacade(new RecordingHandler()), new ScriptedAdapter(withThinking, output: output),
            new AgentRunRequest { AgentId = AgentId, UserId = TestHelper.DefaultTestUserId, UserMessage = "read alpha in the background" });

        var text = string.Concat(chunks.Select(c => c.Text));
        text.ShouldNotContain("alpha.txt 的内容只有一行");
        text.ShouldContain("Started.");
        chunks.ShouldNotContain(c => c.ReasoningText == "sub thoughts");

        var boundary = chunks.FindIndex(c => c.EventType == RunStreamEventTypes.TurnCompleted);
        var finished = chunks.FindIndex(c => c.ToolCalls != null
                                             && c.ToolCalls.Any(t => t.Id == BackgroundCall && t.Task?.Status == CliTaskStatuses.Completed));
        finished.ShouldBeGreaterThan(boundary, "the card of the first answer is updated after the boundary, by its own id");
        chunks.ShouldContain(c => c.ToolCalls != null && c.ToolCalls.Any(t => t.ParentId == BackgroundCall && t.Name == "Read"));
    }
}
