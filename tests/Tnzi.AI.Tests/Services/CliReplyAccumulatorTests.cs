namespace Tnzi.AI.Tests.Services;

/// <summary>
/// One fold for the live stream and for the reply stored when an external run ends: what the user saw streaming is
/// what they see after a reload.
/// </summary>
public class CliReplyAccumulatorTests
{
    [Fact]
    public void WholeTextBlocks_SeparatedByAToolCall_BecomeTwoParagraphs()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(Text("I'll check.")).Text.ShouldBe("I'll check.");
        reply.Apply(ToolUse("c1", "Read"));
        reply.Apply(ToolResult("c1", "contents"));
        reply.Apply(Text("Found it.")).Text.ShouldBe("\n\nFound it.");

        reply.Text.ShouldBe("I'll check.\n\nFound it.");
    }

    [Fact]
    public void FragmentsOfOneBlock_AreJoinedWithoutASeparator()
    {
        // ACP streams a block as fragments; stream-json sends whole blocks. Both read the same once folded.
        var reply = new CliReplyAccumulator();

        reply.Apply(Text("Hel"));
        reply.Apply(new CliAgentEvent { Type = CliAgentEventType.Status, Status = "running" });
        reply.Apply(Text("lo"));

        reply.Text.ShouldBe("Hello", "status and log events do not split a block");
    }

    [Fact]
    public void Thinking_BetweenTwoTextBlocks_SeparatesThem_ButIsNotPartOfTheReply()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(Text("First."));
        reply.Apply(new CliAgentEvent { Type = CliAgentEventType.Thinking, Content = "hmm" });
        reply.Apply(Text("Second."));

        reply.Text.ShouldBe("First.\n\nSecond.");
    }

    [Fact]
    public void ToolCall_IsOneEntry_UpdatedFromRunningToCompleted_WithItsResultAndDuration()
    {
        var reply = new CliReplyAccumulator();
        var start = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);

        reply.Apply(Text("Looking."));
        var started = reply.Apply(new CliAgentEvent
        {
            Type = CliAgentEventType.ToolUse, CallId = "c1", Tool = "screenshot", CreatedAt = start,
            Input = new Dictionary<string, object?> { ["device"] = "dev-7" }
        }).ToolCall!;
        var finished = reply.Apply(new CliAgentEvent
        {
            Type = CliAgentEventType.ToolResult, CallId = "c1", Status = "completed", CreatedAt = start.AddSeconds(90),
            OutputBlocks =
            [
                new CliContentBlock { Type = "text", Text = "ok" },
                new CliContentBlock { Type = "image", MediaType = "image/png", AttachmentId = Guid.Empty }
            ]
        }).ToolCall!;

        started.Status.ShouldBe(ToolCallStatuses.Running);
        started.TextOffset.ShouldBe("Looking.".Length);
        started.Input!.Value.GetProperty("device").GetString().ShouldBe("dev-7");

        finished.Id.ShouldBe("c1");
        finished.Name.ShouldBe("screenshot", "the result is matched back to the call that started it");
        finished.Status.ShouldBe(ToolCallStatuses.Completed);
        finished.DurationMs.ShouldBe(90_000);
        finished.Result!.Select(b => b.Type).ShouldBe(["text", "image"]);

        reply.ToolCalls.ShouldHaveSingleItem().Status.ShouldBe(ToolCallStatuses.Completed);
    }

    [Fact]
    public void ReturnedSnapshots_DoNotChangeWhenTheCallIsUpdatedLater()
    {
        var reply = new CliReplyAccumulator();

        var started = reply.Apply(ToolUse("c1", "Read")).ToolCall!;
        reply.Apply(ToolResult("c1", "done"));

        started.Status.ShouldBe(ToolCallStatuses.Running, "a chunk already handed to the stream must not change under it");
    }

    [Fact]
    public void AFailedResult_CarriesItsTextAsTheError()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(ToolUse("c1", "Bash"));
        var failed = reply.Apply(new CliAgentEvent { Type = CliAgentEventType.ToolResult, CallId = "c1", Status = "failed", Output = "denied" }).ToolCall!;

        failed.Status.ShouldBe(ToolCallStatuses.Failed);
        failed.IsSuccess.ShouldBeFalse();
        failed.Error.ShouldBe("denied");
    }

    [Fact]
    public void AHandedOverFile_IsAFileBlockOfTheResult()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(ToolUse("c1", "hand_over_file"));
        var call = reply.Apply(new CliAgentEvent
        {
            Type = CliAgentEventType.ToolResult, CallId = "c1", Status = "completed", Output = "ready",
            OutputBlocks =
            [
                new CliContentBlock { Type = CliContentBlock.TextType, Text = "ready" },
                new CliContentBlock { Type = CliContentBlock.FileType, Name = "report.pdf", Uri = "https://files.example.com/r.pdf" }
            ]
        }).ToolCall!;

        var file = call.Result.ShouldNotBeNull()[1];
        file.Type.ShouldBe("file");
        file.Name.ShouldBe("report.pdf");
        file.Uri.ShouldBe("https://files.example.com/r.pdf");
        file.Text.ShouldBeNull();
    }

    [Fact]
    public void AResultWhoseCallWasNeverSeen_StillBecomesAnEntry()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(new CliAgentEvent { Type = CliAgentEventType.ToolResult, CallId = "c9", Tool = "Read", Output = "x", Status = "completed" });

        var call = reply.ToolCalls.ShouldHaveSingleItem();
        call.Name.ShouldBe("Read");
        call.Status.ShouldBe(ToolCallStatuses.Completed);
    }

    [Fact]
    public void ASecondStartForTheSameId_CompletesTheEntryInsteadOfAddingOne()
    {
        // ACP reports a call when it is pending and again once its arguments are known
        var reply = new CliReplyAccumulator();

        reply.Apply(new CliAgentEvent { Type = CliAgentEventType.ToolUse, CallId = "c1", Tool = "edit" });
        reply.Apply(new CliAgentEvent
        {
            Type = CliAgentEventType.ToolUse, CallId = "c1", Tool = "edit",
            Input = new Dictionary<string, object?> { ["path"] = "a.txt" }
        });

        reply.ToolCalls.ShouldHaveSingleItem().Input!.Value.GetProperty("path").GetString().ShouldBe("a.txt");
    }

    [Fact]
    public void CallsWithoutIds_AreKeptApart()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(new CliAgentEvent { Type = CliAgentEventType.ToolUse, Tool = "a" });
        reply.Apply(new CliAgentEvent { Type = CliAgentEventType.ToolUse, Tool = "b" });

        reply.ToolCalls.Select(c => c.Name).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void HasContent_IsFalseUntilTextOrAToolCallArrives()
    {
        var reply = new CliReplyAccumulator();
        reply.Apply(new CliAgentEvent { Type = CliAgentEventType.Status, Status = "running" });
        reply.HasContent.ShouldBeFalse();

        reply.Apply(ToolUse("c1", "Read"));
        reply.HasContent.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- sub-agents

    [Fact]
    public void ASubAgentsText_IsAStepOfItsDispatch_NotReplyText()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(Text("Delegating."));
        reply.Apply(ToolUse("agent-1", "Agent"));
        var update = reply.Apply(Text("Sub-agent notes.", parent: "agent-1"));
        reply.Apply(Text("Done.", parent: null));

        update.Text.ShouldBeNull("nothing is appended to the reply");
        update.ToolCall!.Id.ShouldBe("agent-1");
        reply.Text.ShouldBe("Delegating.\n\nDone.");
        var step = reply.ToolCalls.ShouldHaveSingleItem().Task!.Steps.ShouldHaveSingleItem();
        step.Type.ShouldBe(ToolTaskStep.TextType);
        step.Text.ShouldBe("Sub-agent notes.");
    }

    [Fact]
    public void ASubAgentsToolCalls_AreEntriesUnderTheDispatch_WithoutAPlaceInTheReply()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(Text("Delegating."));
        reply.Apply(ToolUse("agent-1", "Agent"));
        reply.Apply(TaskUpdate("agent-1", new CliTask { AgentType = "Explore", Description = "Survey", Background = false }));
        var started = reply.Apply(ToolUse("read-1", "Read", parent: "agent-1"));
        reply.Apply(ToolResult("read-1", "contents", parent: "agent-1"));
        reply.Apply(Text("Notes.", parent: "agent-1"));
        reply.Apply(ToolUse("read-2", "Read", parent: "agent-1"));

        started.ToolCalls.Select(c => c.Id).ShouldBe(["read-1", "agent-1"], "the dispatch gained a step too");

        var calls = reply.ToolCalls;
        calls.Select(c => (c.Id, c.ParentId)).ShouldBe([("agent-1", null), ("read-1", "agent-1"), ("read-2", "agent-1")]);
        calls[0].TextOffset.ShouldBe("Delegating.".Length);
        calls[1].TextOffset.ShouldBeNull("a sub-agent's call has no place in the reply text");
        calls[1].Status.ShouldBe(ToolCallStatuses.Completed);

        var subAgent = calls[0].Task!;
        subAgent.AgentType.ShouldBe("Explore");
        subAgent.Description.ShouldBe("Survey");
        subAgent.Status.ShouldBe(CliTaskStatuses.Running);
        subAgent.Steps.Select(s => s.Type == ToolTaskStep.TextType ? s.Text : s.ToolCallId).ShouldBe(["read-1", "Notes.", "read-2"]);
    }

    [Fact]
    public void ASubAgentsThinking_NeitherSplitsNorJoinsTheReplysBlocks()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(Text("Hel"));
        reply.Apply(new CliAgentEvent { Type = CliAgentEventType.Thinking, Content = "sub thoughts", ParentCallId = "agent-1" });
        reply.Apply(Text("lo"));

        reply.Text.ShouldBe("Hello");
    }

    [Fact]
    public void SubAgentEvents_MergeIntoTheDispatch_AndAnOutcomeIsNotUndoneByALateProgress()
    {
        var reply = new CliReplyAccumulator();
        var start = new DateTime(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc);

        reply.Apply(new CliAgentEvent { Type = CliAgentEventType.ToolUse, CallId = "agent-1", Tool = "Agent", CreatedAt = start });
        reply.Apply(TaskUpdate("agent-1", new CliTask { TaskId = "t1", Description = "Survey", Prompt = "Look around", Background = true }));
        reply.Apply(TaskUpdate("agent-1", new CliTask { Activity = "Reading a.txt", ToolUseCount = 1, TotalTokens = 900, DurationMs = 1200 }));
        reply.Apply(TaskUpdate("agent-1", new CliTask { Status = CliTaskStatuses.Completed, Summary = "All good.", ToolUseCount = 2, DurationMs = 4000 }));
        reply.Apply(TaskUpdate("agent-1", new CliTask { Activity = "late" }));

        var subAgent = reply.ToolCalls.ShouldHaveSingleItem().Task!;
        subAgent.TaskId.ShouldBe("t1");
        subAgent.Description.ShouldBe("Survey", "fields a later event leaves out are kept");
        subAgent.Prompt.ShouldBe("Look around");
        subAgent.Background.ShouldBeTrue();
        subAgent.StartedAt.ShouldBe(start);
        subAgent.ToolUseCount.ShouldBe(2);
        subAgent.TotalTokens.ShouldBe(900);
        subAgent.DurationMs.ShouldBe(4000);
        subAgent.Result.ShouldBe("All good.");
        subAgent.Status.ShouldBe(CliTaskStatuses.Completed);
    }

    [Fact]
    public void ATaskUpdateThatSaysNoStatus_KeepsTheState_AndBringsKindAndModel()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(ToolUse("agent-1", "Agent"));
        reply.Apply(TaskUpdate("agent-1", new CliTask { Kind = CliTaskKinds.Agent, Status = CliTaskStatuses.Running, Description = "Survey" }));
        reply.Apply(TaskUpdate("agent-1", new CliTask { Model = "claude-haiku-5-5" }));
        reply.Apply(ToolUse("bash-1", "Bash"));
        reply.Apply(TaskUpdate("bash-1", new CliTask { Kind = CliTaskKinds.Command, Background = true, Status = CliTaskStatuses.Running }));
        reply.Apply(TaskUpdate("bash-1", new CliTask { Status = CliTaskStatuses.Completed, Summary = "exit code 0" }));

        var calls = reply.ToolCalls;
        calls[0].Task!.Kind.ShouldBe(CliTaskKinds.Agent);
        calls[0].Task!.Model.ShouldBe("claude-haiku-5-5");
        calls[0].Task!.Status.ShouldBe(CliTaskStatuses.Running, "an update without a status changes no state");
        calls[1].Task!.Kind.ShouldBe(CliTaskKinds.Command);
        calls[1].Task!.Status.ShouldBe(CliTaskStatuses.Completed);
        calls[1].Task!.Result.ShouldBe("exit code 0");
    }

    [Theory]
    [InlineData("completed", CliTaskStatuses.Completed)]
    [InlineData("failed", CliTaskStatuses.Failed)]
    public void AForegroundSubAgent_EndsWithItsDispatch_WhenNoOutcomeWasReported(string callStatus, string expected)
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(ToolUse("agent-1", "Agent"));
        reply.Apply(TaskUpdate("agent-1", new CliTask { Background = false }));
        reply.Apply(new CliAgentEvent { Type = CliAgentEventType.ToolResult, CallId = "agent-1", Status = callStatus, Output = "report" });

        reply.ToolCalls.ShouldHaveSingleItem().Task!.Status.ShouldBe(expected);
    }

    [Fact]
    public void ABackgroundSubAgent_KeepsRunning_WhenItsDispatchReturns()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(ToolUse("agent-1", "Agent"));
        reply.Apply(TaskUpdate("agent-1", new CliTask { Background = true }));
        reply.Apply(ToolResult("agent-1", "Async agent launched successfully."));

        var call = reply.ToolCalls.ShouldHaveSingleItem();
        call.Status.ShouldBe(ToolCallStatuses.Completed, "the dispatch itself returned");
        call.Task!.Status.ShouldBe(CliTaskStatuses.Running);
    }

    [Fact]
    public void ANestedSubAgent_HangsUnderTheOneThatDispatchedIt()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(ToolUse("outer", "Agent"));
        reply.Apply(TaskUpdate("outer", new CliTask { Description = "Outer" }));
        reply.Apply(ToolUse("inner", "Agent", parent: "outer"));
        reply.Apply(TaskUpdate("inner", new CliTask { Description = "Inner" }, parent: "outer"));
        reply.Apply(TaskUpdate("inner", new CliTask { Status = CliTaskStatuses.Completed }));

        var calls = reply.ToolCalls;
        calls.Select(c => (c.Id, c.ParentId)).ShouldBe([("outer", null), ("inner", "outer")]);
        calls[0].Task!.Steps.ShouldHaveSingleItem().ToolCallId.ShouldBe("inner");
        calls[1].Task!.Description.ShouldBe("Inner");
        calls[1].Task!.Status.ShouldBe(CliTaskStatuses.Completed);
    }

    [Fact]
    public void ASubAgentWhoseDispatchWasNeverSeen_GetsAPlaceholderInThisTurn()
    {
        // The process was handed over from an earlier run while the sub-agent ran in the background
        var reply = new CliReplyAccumulator();

        reply.Apply(Text("Next question answered."));
        reply.Apply(Text("Found it.", parent: "agent-earlier"));
        reply.Apply(TaskUpdate("agent-earlier", new CliTask { Status = CliTaskStatuses.Completed, Summary = "Found it." }));

        var placeholder = reply.ToolCalls.ShouldHaveSingleItem();
        placeholder.Id.ShouldBe("agent-earlier");
        placeholder.Task!.Status.ShouldBe(CliTaskStatuses.Completed);
        placeholder.Task.Steps.ShouldHaveSingleItem().Text.ShouldBe("Found it.");
        reply.Text.ShouldBe("Next question answered.");
    }

    [Fact]
    public void ABackgroundSubAgentsLaterEvents_StayWithTheTurnThatDispatchedIt()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(ToolUse("agent-1", "Agent"));
        reply.Apply(TaskUpdate("agent-1", new CliTask { Background = true, Description = "Read" }));
        reply.Apply(ToolResult("agent-1", "launched"));
        reply.Apply(Text("Started."));
        reply.BeginTurn();
        reply.EarlierTurnsChanged.ShouldBeEmpty();

        var progress = reply.Apply(ToolUse("read-1", "Read", parent: "agent-1"));
        reply.Apply(Text("Report.", parent: "agent-1"));
        reply.Apply(TaskUpdate("agent-1", new CliTask { Status = CliTaskStatuses.Completed, Summary = "Report." }));
        reply.Apply(Text("It finished."));

        progress.ToolCalls.Select(c => c.Id).ShouldBe(["read-1", "agent-1"], "the stream carries the card's own id");
        reply.Text.ShouldBe("It finished.");
        reply.ToolCalls.ShouldBeEmpty("nothing of the earlier dispatch is copied into the new turn");
        reply.TurnIndex.ShouldBe(1);
        reply.EarlierTurnsChanged.ShouldBe([0]);

        var first = reply.ToolCallsOfTurn(0);
        first.Select(c => c.Id).ShouldBe(["agent-1", "read-1"]);
        first[0].Task!.Status.ShouldBe(CliTaskStatuses.Completed);
        first[0].Task!.Steps.Select(s => s.Type).ShouldBe([ToolTaskStep.ToolType, ToolTaskStep.TextType]);
    }

    [Fact]
    public void SettlingSubAgents_StopsTheOnesStillRunning_InEveryTurn()
    {
        var reply = new CliReplyAccumulator();

        reply.Apply(ToolUse("bg", "Agent"));
        reply.Apply(TaskUpdate("bg", new CliTask { Background = true }));
        reply.Apply(ToolUse("done", "Agent"));
        reply.Apply(TaskUpdate("done", new CliTask { Status = CliTaskStatuses.Completed }));
        reply.BeginTurn();

        var settled = reply.SettleTasks();

        settled.ShouldHaveSingleItem().Id.ShouldBe("bg");
        reply.ToolCallsOfTurn(0).Select(c => c.Task!.Status).ShouldBe([CliTaskStatuses.Stopped, CliTaskStatuses.Completed]);
        reply.EarlierTurnsChanged.ShouldBe([0]);
    }

    [Fact]
    public void TheToolCallsOfAReply_AreCopies_SubAgentStepsIncluded()
    {
        var reply = new CliReplyAccumulator();
        reply.Apply(ToolUse("agent-1", "Agent"));
        reply.Apply(Text("Notes.", parent: "agent-1"));

        reply.ToolCalls[0].Task!.Steps.Clear();

        reply.ToolCalls[0].Task!.Steps.ShouldHaveSingleItem();
    }

    private static CliAgentEvent TaskUpdate(string callId, CliTask subAgent, string? parent = null)
        => new() { Type = CliAgentEventType.Task, CallId = callId, Task = subAgent, ParentCallId = parent };

    private static CliAgentEvent Text(string text, string? parent) => new() { Type = CliAgentEventType.Text, Content = text, ParentCallId = parent };

    private static CliAgentEvent ToolUse(string id, string tool, string? parent)
        => new() { Type = CliAgentEventType.ToolUse, CallId = id, Tool = tool, ParentCallId = parent };

    private static CliAgentEvent ToolResult(string id, string output, string? parent)
        => new() { Type = CliAgentEventType.ToolResult, CallId = id, Status = "completed", Output = output, ParentCallId = parent };

    private static CliAgentEvent Text(string text) => new() { Type = CliAgentEventType.Text, Content = text };

    private static CliAgentEvent ToolUse(string id, string tool) => new() { Type = CliAgentEventType.ToolUse, CallId = id, Tool = tool };

    private static CliAgentEvent ToolResult(string id, string output)
        => new() { Type = CliAgentEventType.ToolResult, CallId = id, Output = output, Status = "completed" };
}
