using System.Text;
using System.Text.Json;

using Tnzi.AI.Cli.Engine;
using Tnzi.AI.Cli.Options;
using Tnzi.AI.Cli.Providers;
using Tnzi.AI.Engine;

namespace Tnzi.AI.Cli.Tests;

public class ClaudeCodeCliProviderTests
{
    private readonly ClaudeCodeCliProvider _provider = new();

    private static CliAgentRequest CreateRequest(
        string? model = null,
        string? sessionId = null,
        IReadOnlyList<string>? allowedTools = null,
        Dictionary<string, string>? envVars = null,
        string workingDirectory = "/tmp/test")
    {
        return new CliAgentRequest
        {
            Prompt = "Hello",
            WorkingDirectory = workingDirectory,
            Model = model,
            SessionId = sessionId,
            AllowedTools = allowedTools,
            EnvironmentVariables = envVars
        };
    }

    private static CliProviderOptions CreateOptions(
        string command = "claude",
        string defaultModel = "",
        Dictionary<string, string>? envVars = null)
    {
        return new CliProviderOptions
        {
            Command = command,
            DefaultModel = defaultModel,
            EnvironmentVariables = envVars ?? new()
        };
    }

    #region BuildProcess Tests

    [Fact]
    public void ProviderName_ShouldBeClaudeCode()
    {
        _provider.ProviderName.ShouldBe("claude-code");
    }

    [Fact]
    public void SupportsSession_ShouldBeTrue()
    {
        _provider.SupportsSession.ShouldBeTrue();
    }

    [Fact]
    public void BuildProcess_ShouldSetCorrectFileName()
    {
        var psi = _provider.BuildProcess(CreateRequest(), CreateOptions(command: "claude"));

        psi.FileName.ShouldBe("claude");
    }

    [Fact]
    public void BuildProcess_ShouldIncludeStreamJsonFlag()
    {
        var psi = _provider.BuildProcess(CreateRequest(), CreateOptions());

        psi.ArgumentList.ShouldContain("--output-format");
        psi.ArgumentList.ShouldContain("stream-json");
    }

    [Fact]
    public void BuildProcess_ShouldIncludeModel_WhenSpecified()
    {
        var psi = _provider.BuildProcess(
            CreateRequest(model: "claude-sonnet-4-20250514"),
            CreateOptions());

        psi.ArgumentList.ShouldContain("--model");
        psi.ArgumentList.ShouldContain("claude-sonnet-4-20250514");
    }

    [Fact]
    public void BuildProcess_ShouldIncludeResume_WhenSessionIdProvided()
    {
        var psi = _provider.BuildProcess(
            CreateRequest(sessionId: "sess-abc123"),
            CreateOptions());

        psi.ArgumentList.ShouldContain("--resume");
        psi.ArgumentList.ShouldContain("sess-abc123");
    }

    [Fact]
    public void BuildProcess_ShouldRedirectStdinStdoutStderr()
    {
        var psi = _provider.BuildProcess(CreateRequest(), CreateOptions());

        psi.RedirectStandardInput.ShouldBeTrue();
        psi.RedirectStandardOutput.ShouldBeTrue();
        psi.RedirectStandardError.ShouldBeTrue();
    }

    [Fact]
    public void BuildProcess_ShouldSetWorkingDirectory()
    {
        var psi = _provider.BuildProcess(
            CreateRequest(workingDirectory: "/home/user/project"),
            CreateOptions());

        psi.WorkingDirectory.ShouldBe("/home/user/project");
    }

    [Fact]
    public void BuildProcess_ShouldMergeEnvironmentVariables()
    {
        var providerEnv = new Dictionary<string, string>
        {
            ["ANTHROPIC_API_KEY"] = "sk-provider",
            ["SHARED_VAR"] = "from-provider"
        };
        var requestEnv = new Dictionary<string, string>
        {
            ["CUSTOM_VAR"] = "custom-value",
            ["SHARED_VAR"] = "from-request" // 请求级覆盖 Provider 级
        };

        var psi = _provider.BuildProcess(
            CreateRequest(envVars: requestEnv),
            CreateOptions(envVars: providerEnv));

        psi.Environment["ANTHROPIC_API_KEY"].ShouldBe("sk-provider");
        psi.Environment["CUSTOM_VAR"].ShouldBe("custom-value");
        psi.Environment["SHARED_VAR"].ShouldBe("from-request");
    }

    #endregion

    #region ParseOutputAsync Tests

    private async Task<List<CliAgentEvent>> ParseJsonLines(params string[] lines)
    {
        var json = string.Join("\n", lines);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var reader = new StreamReader(stream, Encoding.UTF8);

        var events = new List<CliAgentEvent>();
        await foreach (var evt in _provider.ParseOutputAsync(reader, CancellationToken.None))
        {
            events.Add(evt);
        }
        return events;
    }

    [Fact]
    public async Task ParseOutput_ShouldParseTextEvent()
    {
        var events = await ParseJsonLines(
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Hello world"}]}}""");

        events.ShouldHaveSingleItem();
        events[0].EventType.ShouldBe(CliEventType.Text);
        events[0].Content.ShouldBe("Hello world");
    }

    [Fact]
    public async Task ParseOutput_ShouldParseThinkingEvent()
    {
        var events = await ParseJsonLines(
            """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"Let me think..."}]}}""");

        events.ShouldHaveSingleItem();
        events[0].EventType.ShouldBe(CliEventType.Thinking);
        events[0].Content.ShouldBe("Let me think...");
    }

    [Fact]
    public async Task ParseOutput_ShouldParseToolUseEvent()
    {
        var events = await ParseJsonLines(
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"tool_1","name":"Read","input":{"path":"/tmp"}}]}}""");

        events.ShouldHaveSingleItem();
        events[0].EventType.ShouldBe(CliEventType.ToolUse);
        events[0].ToolName.ShouldBe("Read");
        events[0].ToolId.ShouldBe("tool_1");
    }

    [Fact]
    public async Task ParseOutput_ShouldParseToolResultEvent()
    {
        var events = await ParseJsonLines(
            """{"type":"tool_result","tool_use_id":"tool_1","is_error":false,"content":"file contents here"}""");

        events.ShouldHaveSingleItem();
        events[0].EventType.ShouldBe(CliEventType.ToolResult);
        events[0].Content.ShouldBe("file contents here");
        events[0].ToolId.ShouldBe("tool_1");
    }

    [Fact]
    public async Task ParseOutput_ShouldParseInitEvent_WithSessionId()
    {
        var events = await ParseJsonLines(
            """{"type":"system","subtype":"init","session_id":"sess-xyz","model":"claude-sonnet-4-20250514","tools":["Read","Write"]}""");

        events.ShouldHaveSingleItem();
        events[0].EventType.ShouldBe(CliEventType.Status);
        events[0].SessionId.ShouldBe("sess-xyz");
        events[0].Content.ShouldBe("init");
    }

    [Fact]
    public async Task ParseOutput_ShouldParseResultEvent()
    {
        var events = await ParseJsonLines(
            """{"type":"result","result":"Task completed successfully","is_error":false,"total_cost_usd":0.01,"duration_ms":5000,"num_turns":3}""");

        // result 事件生成两个 CliAgentEvent: Text + Complete
        events.Count.ShouldBe(2);
        events[0].EventType.ShouldBe(CliEventType.Text);
        events[0].Content.ShouldBe("Task completed successfully");
        events[1].EventType.ShouldBe(CliEventType.Complete);
        events[1].Metadata.ShouldNotBeNull();
        events[1].Metadata!.ContainsKey("total_cost_usd").ShouldBeTrue();
    }

    [Fact]
    public async Task ParseOutput_ShouldSkipNonJsonLines()
    {
        var events = await ParseJsonLines(
            "some debug output",
            "",
            "   ",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"valid"}]}}""",
            "another non-json line");

        events.ShouldHaveSingleItem();
        events[0].EventType.ShouldBe(CliEventType.Text);
        events[0].Content.ShouldBe("valid");
    }

    #endregion

    #region MapToStreamChunk Tests

    [Fact]
    public void MapToStreamChunk_Text_ShouldSetTextProperty()
    {
        var evt = new CliAgentEvent { EventType = CliEventType.Text, Content = "Hello" };

        var chunk = _provider.MapToStreamChunk(evt);

        chunk.ShouldNotBeNull();
        chunk!.Text.ShouldBe("Hello");
        chunk.IsToolCall.ShouldBeFalse();
    }

    [Fact]
    public void MapToStreamChunk_Thinking_ShouldSetReasoningText()
    {
        var evt = new CliAgentEvent { EventType = CliEventType.Thinking, Content = "Reasoning..." };

        var chunk = _provider.MapToStreamChunk(evt);

        chunk.ShouldNotBeNull();
        chunk!.ReasoningText.ShouldBe("Reasoning...");
        chunk.Text.ShouldBeNull();
    }

    [Fact]
    public void MapToStreamChunk_ToolUse_ShouldSetIsToolCall()
    {
        var evt = new CliAgentEvent { EventType = CliEventType.ToolUse, ToolName = "Read" };

        var chunk = _provider.MapToStreamChunk(evt);

        chunk.ShouldNotBeNull();
        chunk!.IsToolCall.ShouldBeTrue();
        chunk.ToolCallNames.ShouldNotBeNull();
        chunk.ToolCallNames!.ShouldContain("Read");
    }

    [Fact]
    public void MapToStreamChunk_Error_ShouldSetErrorProperty()
    {
        var evt = new CliAgentEvent { EventType = CliEventType.Error, Content = "Something failed" };

        var chunk = _provider.MapToStreamChunk(evt);

        chunk.ShouldNotBeNull();
        chunk!.Error.ShouldBe("Something failed");
    }

    [Fact]
    public void MapToStreamChunk_Status_ShouldReturnNull()
    {
        var evt = new CliAgentEvent { EventType = CliEventType.Status, Content = "init" };

        var chunk = _provider.MapToStreamChunk(evt);

        chunk.ShouldBeNull();
    }

    [Fact]
    public void MapToStreamChunk_Complete_ShouldSetFinishReason()
    {
        var evt = new CliAgentEvent { EventType = CliEventType.Complete };

        var chunk = _provider.MapToStreamChunk(evt);

        chunk.ShouldNotBeNull();
        chunk!.FinishReason.ShouldBe("stop");
    }

    #endregion
}
