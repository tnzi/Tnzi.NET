namespace Tnzi.AI.Tests;

/// <summary>
/// StreamEvent 序列化测试 — 验证错误字段和工具调用字段正确序列化
/// </summary>
public class StreamEventTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void ErrorEvent_SerializesAllErrorFields()
    {
        var evt = new StreamEvent
        {
            IsError = true,
            ErrorMessage = "Provider timeout",
            ErrorCode = "AI:ChatFailed"
        };

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<StreamEvent>(json, JsonOptions);

        deserialized.ShouldNotBeNull();
        deserialized.IsError.ShouldBeTrue();
        deserialized.ErrorMessage.ShouldBe("Provider timeout");
        deserialized.ErrorCode.ShouldBe("AI:ChatFailed");
    }

    [Fact]
    public void DeltaEvent_DoesNotIncludeErrorFields()
    {
        var evt = new StreamEvent
        {
            Delta = "Hello",
            Model = "gpt-4"
        };

        var json = JsonSerializer.Serialize(evt, JsonOptions);

        json.ShouldContain("\"delta\"");
        json.ShouldContain("\"model\"");
        // Default false booleans should still be present (not nullable)
        json.ShouldNotContain("\"errorMessage\"");
        json.ShouldNotContain("\"errorCode\"");
    }

    [Fact]
    public void ToolCallEvent_SerializesIsToolCall()
    {
        var evt = new StreamEvent
        {
            IsToolCall = true,
            Model = "gpt-4"
        };

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<StreamEvent>(json, JsonOptions);

        deserialized.ShouldNotBeNull();
        deserialized.IsToolCall.ShouldBeTrue();
        deserialized.Model.ShouldBe("gpt-4");
    }

    [Fact]
    public void DoneEvent_SerializesUsageAndFinishReason()
    {
        var evt = new StreamEvent
        {
            IsDone = true,
            FinishReason = "stop",
            Model = "gpt-4",
            Usage = new TokenUsageDto
            {
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150
            }
        };

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<StreamEvent>(json, JsonOptions);

        deserialized.ShouldNotBeNull();
        deserialized.IsDone.ShouldBeTrue();
        deserialized.FinishReason.ShouldBe("stop");
        deserialized.Usage.ShouldNotBeNull();
        deserialized.Usage!.TotalTokens.ShouldBe(150);
    }

    [Fact]
    public void DefaultEvent_HasCorrectDefaults()
    {
        var evt = new StreamEvent();

        evt.Delta.ShouldBeNull();
        evt.FinishReason.ShouldBeNull();
        evt.Model.ShouldBeNull();
        evt.ThreadId.ShouldBeNull();
        evt.Usage.ShouldBeNull();
        evt.IsDone.ShouldBeFalse();
        evt.IsError.ShouldBeFalse();
        evt.ErrorMessage.ShouldBeNull();
        evt.ErrorCode.ShouldBeNull();
        evt.IsToolCall.ShouldBeFalse();
    }

    [Fact]
    public void StructuredEvent_SerializesEventDataSuggestionsTodosAndArtifacts()
    {
        var runId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var evt = new StreamEvent
        {
            EventType = "clarification",
            EventData = new Dictionary<string, object>
            {
                ["type"] = "ApproachChoice"
            },
            Suggestions = ["Pick option A"],
            Todos =
            [
                new TodoItemDto
                {
                    Content = "Review design",
                    Status = TodoStatus.InProgress,
                    Order = 1
                }
            ],
            Artifacts =
            [
                new AgentArtifactDto
                {
                    RunId = runId,
                    ThreadId = threadId,
                    FileName = "plan.md",
                    VirtualPath = "/artifacts/plan.md"
                }
            ]
        };

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<StreamEvent>(json, JsonOptions);

        json.ShouldContain("\"eventType\":\"clarification\"");
        json.ShouldContain("\"eventData\"");
        json.ShouldContain("\"suggestions\"");
        json.ShouldContain("\"todos\"");
        json.ShouldContain("\"artifacts\"");
        deserialized.ShouldNotBeNull();
        deserialized.EventType.ShouldBe("clarification");
        deserialized.Suggestions.ShouldNotBeNull();
        deserialized.Suggestions.ShouldContain("Pick option A");
        deserialized.Todos.ShouldNotBeNull();
        deserialized.Todos.Count.ShouldBe(1);
        deserialized.Artifacts.ShouldNotBeNull();
        deserialized.Artifacts.Count.ShouldBe(1);
    }
}
