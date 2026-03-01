namespace Tnzi.AI.Tests;

/// <summary>
/// PruneChatReducer 单元测试 — 验证历史消息裁剪策略
/// </summary>
public class PruneChatReducerTests
{
    [Fact]
    public async Task Reduce_SystemMessagesAlwaysPreserved()
    {
        var options = new PruneOptions { KeepLastTurns = 1 };
        var reducer = CreateReducer(options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "Old question"),
            new(ChatRole.Assistant, "Old answer"),
            new(ChatRole.User, "New question"),
            new(ChatRole.Assistant, "New answer")
        };

        var result = await reducer.ReduceAsync(messages);

        // 系统消息应始终保留
        result.ShouldContain(m => m.Role == ChatRole.System && m.Text == "You are a helpful assistant.");
        // 只保留最后 1 轮
        result.ShouldContain(m => m.Text == "New question");
        result.ShouldContain(m => m.Text == "New answer");
        // 旧消息被裁剪
        result.ShouldNotContain(m => m.Text == "Old question");
        result.ShouldNotContain(m => m.Text == "Old answer");
    }

    [Fact]
    public async Task Reduce_KeepsLastNTurns()
    {
        var options = new PruneOptions { KeepLastTurns = 2 };
        var reducer = CreateReducer(options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Turn 1"),
            new(ChatRole.Assistant, "Reply 1"),
            new(ChatRole.User, "Turn 2"),
            new(ChatRole.Assistant, "Reply 2"),
            new(ChatRole.User, "Turn 3"),
            new(ChatRole.Assistant, "Reply 3")
        };

        var result = await reducer.ReduceAsync(messages);

        // 保留最后 2 轮
        result.ShouldContain(m => m.Text == "Turn 2");
        result.ShouldContain(m => m.Text == "Reply 2");
        result.ShouldContain(m => m.Text == "Turn 3");
        result.ShouldContain(m => m.Text == "Reply 3");
        // 第一轮被裁剪
        result.ShouldNotContain(m => m.Text == "Turn 1");
        result.ShouldNotContain(m => m.Text == "Reply 1");
    }

    [Fact]
    public async Task Reduce_EmptyHistory_ReturnsEmpty()
    {
        var options = new PruneOptions { KeepLastTurns = 5 };
        var reducer = CreateReducer(options);

        var result = await reducer.ReduceAsync([]);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reduce_UnderLimit_NoChange()
    {
        var options = new PruneOptions { KeepLastTurns = 10 };
        var reducer = CreateReducer(options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System prompt"),
            new(ChatRole.User, "Question"),
            new(ChatRole.Assistant, "Answer")
        };

        var result = await reducer.ReduceAsync(messages);

        // 未超限，所有消息保留
        result.Count.ShouldBe(3);
        result[0].Text.ShouldBe("System prompt");
        result[1].Text.ShouldBe("Question");
        result[2].Text.ShouldBe("Answer");
    }

    [Fact]
    public async Task Reduce_MultipleSystemMessages_AllPreserved()
    {
        var options = new PruneOptions { KeepLastTurns = 1 };
        var reducer = CreateReducer(options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System 1"),
            new(ChatRole.System, "System 2"),
            new(ChatRole.User, "Old turn"),
            new(ChatRole.Assistant, "Old reply"),
            new(ChatRole.User, "Last turn"),
            new(ChatRole.Assistant, "Last reply")
        };

        var result = await reducer.ReduceAsync(messages);

        // 所有系统消息保留
        result.Count(m => m.Role == ChatRole.System).ShouldBe(2);
        result.ShouldContain(m => m.Text == "System 1");
        result.ShouldContain(m => m.Text == "System 2");
    }

    [Fact]
    public async Task Reduce_DropToolOutputsOlderThan_RemovesOldToolMessages()
    {
        var options = new PruneOptions
        {
            KeepLastTurns = 3,
            DropToolOutputsOlderThan = 1
        };
        var reducer = CreateReducer(options);

        // 构建包含工具消息的历史
        var toolResult = new FunctionResultContent("call_1", "some result");
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Turn 1"),
            new(ChatRole.Assistant, "Calling tool..."),
            new(ChatRole.Tool, [toolResult]),  // 第一轮的工具输出（turnAge=3，大于阈值1）
            new(ChatRole.User, "Turn 2"),
            new(ChatRole.Assistant, "Reply 2"),
            new(ChatRole.User, "Turn 3"),
            new(ChatRole.Assistant, "Reply 3")
        };

        var result = await reducer.ReduceAsync(messages);

        // 旧工具消息应被裁剪
        result.ShouldNotContain(m => m.Role == ChatRole.Tool);
    }

    private static PruneChatReducer CreateReducer(PruneOptions options)
    {
        return new PruneChatReducer(options, Mock.Of<ILogger<PruneChatReducer>>());
    }
}
