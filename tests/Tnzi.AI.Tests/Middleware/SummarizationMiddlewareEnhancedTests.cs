namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// SummarizationMiddleware 增强功能测试 - MicroCompact + 9节摘要
/// </summary>
public class SummarizationMiddlewareEnhancedTests
{
    #region MicroCompact

    [Fact]
    public void MicroCompact_OldToolResults_AreCleared()
    {
        // 构建消息列表：前面的工具结果应被清理，最近的保留
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first question"),
            new(ChatRole.Assistant, "let me search"),
            new(ChatRole.Tool, [new FunctionResultContent("call_1", "old data that should be cleared")]),
            new(ChatRole.User, "second question"),
            new(ChatRole.Assistant, "searching more"),
            new(ChatRole.Tool, [new FunctionResultContent("call_2", "medium data")]),
            new(ChatRole.User, "third question"),
            new(ChatRole.Assistant, "found it"),
            new(ChatRole.Tool, [new FunctionResultContent("call_3", "recent data")]),
            new(ChatRole.User, "what next?")
        };

        // 保��最近 2 个工具结果消息
        var result = SummarizationMiddleware.MicroCompact(messages, keepRecentToolResults: 2);

        result.ShouldNotBeSameAs(messages);
        result.Count.ShouldBe(messages.Count); // 消息数不变

        // call_1 被清理（距末尾最远）
        var frc1 = result[2].Contents.OfType<FunctionResultContent>().First();
        (frc1.Result as string).ShouldBe("[tool result cleared]");

        // call_2 保留（在最近 2 个工具结果内）
        var frc2 = result[5].Contents.OfType<FunctionResultContent>().First();
        (frc2.Result as string).ShouldBe("medium data");

        // call_3 保留（最近）
        var frc3 = result[8].Contents.OfType<FunctionResultContent>().First();
        (frc3.Result as string).ShouldBe("recent data");
    }

    [Fact]
    public void MicroCompact_AllResultsWithinLimit_NoChanges()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("call_1", "data1")]),
            new(ChatRole.Tool, [new FunctionResultContent("call_2", "data2")])
        };

        var result = SummarizationMiddleware.MicroCompact(messages, keepRecentToolResults: 5);

        result.ShouldBeSameAs(messages); // 未修改
    }

    [Fact]
    public void MicroCompact_NoToolResults_NoChanges()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hello"),
            new(ChatRole.Assistant, "hi there")
        };

        var result = SummarizationMiddleware.MicroCompact(messages, keepRecentToolResults: 3);

        result.ShouldBeSameAs(messages);
    }

    [Fact]
    public void MicroCompact_OriginalMessagesNotMutated()
    {
        var originalData = "original data";
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("call_old", originalData)]),
            new(ChatRole.User, "q1"),
            new(ChatRole.User, "q2"),
            new(ChatRole.User, "q3"),
            new(ChatRole.Tool, [new FunctionResultContent("call_new", "new data")])
        };

        _ = SummarizationMiddleware.MicroCompact(messages, keepRecentToolResults: 1);

        // 原始消息未被修改
        var original = messages[0].Contents.OfType<FunctionResultContent>().First();
        (original.Result as string).ShouldBe(originalData);
    }

    [Fact]
    public void MicroCompact_AlreadyClearedResults_NotDoubleCleaned()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("call_1", "[tool result cleared]")]),
            new(ChatRole.User, "next question"),
            new(ChatRole.Tool, [new FunctionResultContent("call_2", "active data")])
        };

        // 即使 keepRecentToolResults=1，已清理的不应再处理
        var result = SummarizationMiddleware.MicroCompact(messages, keepRecentToolResults: 1);

        // call_1 已经是 cleared，不变
        result.ShouldBeSameAs(messages);
    }

    #endregion

    #region SummarizationOptions 默认值

    [Fact]
    public void SummarizationOptions_DefaultFraction_Is093()
    {
        var opts = new SummarizationOptions();
        opts.Trigger.FractionThreshold.ShouldBe(0.93);
    }

    [Fact]
    public void SummarizationOptions_MicroCompact_DefaultEnabled()
    {
        var opts = new SummarizationOptions();
        opts.EnableMicroCompact.ShouldBeTrue();
        opts.KeepRecentToolResults.ShouldBe(5);
    }

    #endregion
}
