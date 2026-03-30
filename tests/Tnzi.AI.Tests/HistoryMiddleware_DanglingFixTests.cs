namespace Tnzi.AI.Tests;

public class HistoryMiddleware_DanglingFixTests
{
    [Fact]
    public void PatchDanglingToolCalls_NoDangling_NoChanges()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hello"),
            new(ChatRole.Assistant, "hi")
        };

        var patched = HistoryMiddleware.PatchDanglingToolCalls(messages);
        Assert.Equal(messages.Count, patched.Count);
    }

    [Fact]
    public void PatchDanglingToolCalls_WithDangling_InjectsSyntheticResult()
    {
        var callId = "call-123";
        var assistantMsg = new ChatMessage(ChatRole.Assistant, [
            new FunctionCallContent(callId, "search", new Dictionary<string, object?> { ["q"] = "test" })
        ]);

        var messages = new List<ChatMessage> { new(ChatRole.User, "search for test"), assistantMsg };

        var patched = HistoryMiddleware.PatchDanglingToolCalls(messages);

        // Should have 3 messages: user, assistant, synthetic tool result
        Assert.Equal(3, patched.Count);
        var resultMsg = patched[2];
        Assert.Equal(ChatRole.Tool, resultMsg.Role);
        var frc = resultMsg.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        Assert.NotNull(frc);
        Assert.Contains("interrupted", frc.Result?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PatchDanglingToolCalls_WithMatchingResult_NoChanges()
    {
        var callId = "call-456";
        var assistantMsg = new ChatMessage(ChatRole.Assistant, [
            new FunctionCallContent(callId, "search", new Dictionary<string, object?> { ["q"] = "test" })
        ]);
        var toolResult = new ChatMessage(ChatRole.Tool, [
            new FunctionResultContent(callId, "result data")
        ]);

        var messages = new List<ChatMessage> { new(ChatRole.User, "hi"), assistantMsg, toolResult };

        var patched = HistoryMiddleware.PatchDanglingToolCalls(messages);
        Assert.Equal(3, patched.Count);
    }

    [Fact]
    public void PatchDanglingToolCalls_MultipleDangling_InjectsAll()
    {
        var assistantMsg = new ChatMessage(ChatRole.Assistant, [
            new FunctionCallContent("call-1", "search", new Dictionary<string, object?> { ["q"] = "a" }),
            new FunctionCallContent("call-2", "fetch", new Dictionary<string, object?> { ["url"] = "b" })
        ]);

        var messages = new List<ChatMessage> { new(ChatRole.User, "test"), assistantMsg };

        var patched = HistoryMiddleware.PatchDanglingToolCalls(messages);
        // user + assistant + 2 synthetic results
        Assert.Equal(4, patched.Count);
    }
}
