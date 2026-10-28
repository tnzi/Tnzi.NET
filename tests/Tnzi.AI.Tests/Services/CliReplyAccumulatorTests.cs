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

    private static CliAgentEvent Text(string text) => new() { Type = CliAgentEventType.Text, Content = text };

    private static CliAgentEvent ToolUse(string id, string tool) => new() { Type = CliAgentEventType.ToolUse, CallId = id, Tool = tool };

    private static CliAgentEvent ToolResult(string id, string output)
        => new() { Type = CliAgentEventType.ToolResult, CallId = id, Output = output, Status = "completed" };
}
