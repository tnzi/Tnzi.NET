namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// What the stream-json adapter sends and reads for chat: attachments as content blocks, tool results with their
/// images, structured tool input, the session entry a later rewind cuts back to, and the cut itself.
/// </summary>
/// <remarks>The frame shapes were checked against claude 2.1.303 (see the smoke tests for the live checks).</remarks>
public class StreamJsonChatContentTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static CliAgentLaunchContext Context() => new()
    {
        Provider = CliBuiltInProviders.All["claude"],
        ExecutablePath = "/nonexistent/claude",
        Prompt = "what is in these?",
        WorkingDirectory = "/tmp/does-not-need-to-exist"
    };

    private static StreamJsonAdapter Adapter() => new(NullLogger<StreamJsonAdapter>.Instance);

    // ---------------------------------------------------------------- input

    [Fact]
    public void TheUserFrame_PutsAttachmentsFirst_AsImageAndDocumentBlocks_ThenTheText()
    {
        var frame = StreamJsonAdapter.BuildUserFrame("what is in these?",
        [
            new CliPromptAttachment("image/png", "screen.png", Png),
            new CliPromptAttachment("application/pdf", "contract.pdf", [0x25, 0x50, 0x44, 0x46])
        ]);

        using var doc = JsonDocument.Parse(frame);
        var content = doc.RootElement.GetProperty("message").GetProperty("content");
        content.GetArrayLength().ShouldBe(3);

        content[0].GetProperty("type").GetString().ShouldBe("image");
        content[0].GetProperty("source").GetProperty("type").GetString().ShouldBe("base64");
        content[0].GetProperty("source").GetProperty("media_type").GetString().ShouldBe("image/png");
        content[0].GetProperty("source").GetProperty("data").GetString().ShouldBe(Convert.ToBase64String(Png));

        content[1].GetProperty("type").GetString().ShouldBe("document");
        content[1].GetProperty("source").GetProperty("media_type").GetString().ShouldBe("application/pdf");
        content[1].GetProperty("title").GetString().ShouldBe("contract.pdf");

        content[2].GetProperty("type").GetString().ShouldBe("text");
        content[2].GetProperty("text").GetString().ShouldBe("what is in these?");
    }

    [Fact]
    public void AnAttachmentOnlyMessage_SendsNoEmptyTextBlock()
    {
        var frame = StreamJsonAdapter.BuildUserFrame("", [new CliPromptAttachment("image/png", null, Png)]);

        using var doc = JsonDocument.Parse(frame);
        var content = doc.RootElement.GetProperty("message").GetProperty("content");
        content.GetArrayLength().ShouldBe(1, "the API refuses an empty text block");
    }

    [Fact]
    public async Task Run_SendsTheLaunchContextsAttachments()
    {
        var transport = new FakeCliAgentTransport(
            ["""{"type":"result","subtype":"success","session_id":"s","result":"ok","num_turns":1}"""]);

        await foreach (var _ in Adapter().RunAsync(transport,
                           Context() with { Attachments = [new CliPromptAttachment("image/png", "a.png", Png)] }, CancellationToken.None))
        {
        }

        transport.Written[0].ShouldContain("\"type\":\"image\"");
    }

    // ---------------------------------------------------------------- resume at an entry

    [Fact]
    public void BuildProcess_CutsTheSessionBack_WhenAResumeEntryIsGiven()
    {
        var spec = Adapter().BuildProcess(Context() with { ResumeSessionId = "s1", ResumeAtEntryId = "e1" });

        var args = spec.Arguments.ToList();
        args.IndexOf("--resume-session-at").ShouldBe(args.IndexOf("--resume") + 2);
        args[args.IndexOf("--resume-session-at") + 1].ShouldBe("e1");
        args.ShouldContain("--fork-session", "the cut goes into a new session; the original stays as it was");
    }

    [Fact]
    public void BuildProcess_PlainResume_DoesNotFork()
    {
        var spec = Adapter().BuildProcess(Context() with { ResumeSessionId = "s1" });

        spec.Arguments.ShouldNotContain("--fork-session");
        spec.Arguments.ShouldNotContain("--resume-session-at");
    }

    [Fact]
    public void BuildProcess_UsersCannotPassTheirOwnCutArguments()
    {
        var spec = Adapter().BuildProcess(Context() with { CustomArgs = ["--resume-session-at", "x", "--fork-session"] });

        spec.Arguments.ShouldNotContain("--resume-session-at");
        spec.Arguments.ShouldNotContain("--fork-session");
    }

    [Fact]
    public async Task TheLastConversationEntry_IsReportedForLaterRewinds_NotSystemOrResultFrames()
    {
        var transport = new FakeCliAgentTransport(
        [
            """{"type":"system","subtype":"init","session_id":"s1","uuid":"sys-1"}""",
            """{"type":"assistant","uuid":"a-1","session_id":"s1","message":{"role":"assistant","model":"m","content":[{"type":"tool_use","id":"t1","name":"Read","input":{}}]}}""",
            """{"type":"user","uuid":"u-1","session_id":"s1","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"x"}]}}""",
            """{"type":"assistant","uuid":"a-2","session_id":"s1","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"done"}]}}""",
            """{"type":"result","subtype":"success","uuid":"r-1","session_id":"s1","result":"done","num_turns":2}"""
        ]);
        var adapter = Adapter();

        await foreach (var _ in adapter.RunAsync(transport, Context(), CancellationToken.None))
        {
        }

        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).SessionEntryId.ShouldBe("a-2");
    }

    [Fact]
    public async Task AnUnknownCutPoint_IsTreatedAsARejectedResume_SoTheRunRetriesWithAFreshSession()
    {
        var transport = new FakeCliAgentTransport(
            ["""{"type":"result","subtype":"error_during_execution","is_error":true,"result":"","num_turns":0}"""],
            stderrTail: "No message found with message.uuid of: 11111111-2222-4333-8444-555555555555");
        var adapter = Adapter();

        await foreach (var _ in adapter.RunAsync(transport, Context() with { ResumeSessionId = "s1", ResumeAtEntryId = "11111111-2222-4333-8444-555555555555" }, CancellationToken.None))
        {
        }

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1, StderrTail = transport.StderrTail });
        result.ResumeRejected.ShouldBeTrue();
        result.SessionEntryId.ShouldBeNull();
    }

    // ---------------------------------------------------------------- output

    [Fact]
    public async Task ToolResults_KeepTheirTextAndImageBlocks_AndTheTextFormLeavesTheBytesOut()
    {
        var data = Convert.ToBase64String(Png);
        var transport = new FakeCliAgentTransport(
        [
            $$$"""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":[{"type":"text","text":"captured"},{"type":"image","source":{"type":"base64","data":"{{{data}}}","media_type":"image/png"}}]}]}}""",
            """{"type":"result","subtype":"success","result":"ok","num_turns":1}"""
        ]);

        var events = new List<CliAgentEvent>();
        await foreach (var evt in Adapter().RunAsync(transport, Context(), CancellationToken.None))
        {
            events.Add(evt);
        }

        var result = events.Single(e => e.Type == CliAgentEventType.ToolResult);
        result.CallId.ShouldBe("t1");
        var blocks = result.OutputBlocks.ShouldNotBeNull();
        blocks.Select(b => b.Type).ShouldBe(["text", "image"]);
        blocks[1].MediaType.ShouldBe("image/png");
        blocks[1].Data.ShouldBe(data);
        result.Output.ShouldBe("captured\n[image: image/png]");
        result.Output.ShouldNotBeNull().ShouldNotContain(data);
    }

    [Fact]
    public void ReadBlocks_AStringResult_IsOneTextBlock_AndUnknownBlocksAreKeptAsText()
    {
        using var text = JsonDocument.Parse("\"plain\"");
        StreamJsonAdapter.ReadBlocks(text.RootElement).ShouldHaveSingleItem().Text.ShouldBe("plain");

        using var mixed = JsonDocument.Parse("""[{"type":"audio","data":"AAAA"}]""");
        StreamJsonAdapter.ReadBlocks(mixed.RootElement).ShouldHaveSingleItem().Text!.ShouldContain("audio");
    }

    [Fact]
    public void AFlattenedResourceLink_IsAFileBlock_AndOrdinaryTextIsNot()
    {
        using var flattened = JsonDocument.Parse("""[{"type":"text","text":"done"},{"type":"text","text":"[Resource link: report.pdf] https://files.example.com/u/42/report.pdf"}]""");

        var blocks = StreamJsonAdapter.ReadBlocks(flattened.RootElement);

        blocks.Select(b => b.Type).ShouldBe(["text", "file"]);
        blocks[1].Name.ShouldBe("report.pdf");
        blocks[1].Uri.ShouldBe("https://files.example.com/u/42/report.pdf");
        StreamJsonAdapter.RenderBlocks(blocks).ShouldBe("done\n[file: report.pdf] https://files.example.com/u/42/report.pdf");

        StreamJsonAdapter.ReadFlattenedResourceLink("See [Resource link: a.pdf] https://x/a.pdf for details").ShouldBeNull(
            "only a block that is exactly the flattened line is a file");
    }

    [Fact]
    public void ANativeResourceLink_IsAFileBlock_WithItsMediaType()
    {
        using var native = JsonDocument.Parse("""[{"type":"resource_link","uri":"storage://files/9","name":"notes.txt","mimeType":"text/plain"}]""");

        var file = StreamJsonAdapter.ReadBlocks(native.RootElement).ShouldHaveSingleItem();

        file.Type.ShouldBe(CliContentBlock.FileType);
        file.Uri.ShouldBe("storage://files/9");
        file.Name.ShouldBe("notes.txt");
        file.MediaType.ShouldBe("text/plain");
    }

    [Fact]
    public async Task NestedToolArguments_StayStructured()
    {
        var transport = new FakeCliAgentTransport(
        [
            """{"type":"assistant","message":{"role":"assistant","model":"m","content":[{"type":"tool_use","id":"t1","name":"MultiEdit","input":{"path":"a.txt","edits":[{"old":"x","new":"y"}]}}]}}""",
            """{"type":"result","subtype":"success","result":"ok","num_turns":1}"""
        ]);

        var events = new List<CliAgentEvent>();
        await foreach (var evt in Adapter().RunAsync(transport, Context(), CancellationToken.None))
        {
            events.Add(evt);
        }

        var input = events.Single(e => e.Type == CliAgentEventType.ToolUse).Input!;
        var serialized = JsonSerializer.Serialize(input);
        using var round = JsonDocument.Parse(serialized);
        round.RootElement.GetProperty("edits").ValueKind.ShouldBe(JsonValueKind.Array,
            "an array argument used to come out as a string of JSON");
        round.RootElement.GetProperty("edits")[0].GetProperty("new").GetString().ShouldBe("y");
    }
}
