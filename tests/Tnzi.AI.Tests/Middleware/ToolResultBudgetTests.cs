namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// 工具结果预算管理测试 - 验证 HistoryMiddleware.TruncateLargeToolResults
/// </summary>
public class ToolResultBudgetTests
{
    [Fact]
    public void TruncateLargeToolResults_LargeResult_IsTruncated()
    {
        var largeText = new string('x', 50_000);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("call_1", largeText)])
        };

        var result = HistoryMiddleware.TruncateLargeToolResults(messages, maxChars: 30_000, previewChars: 2_000);

        result.ShouldNotBeSameAs(messages); // 新列表
        var frc = result[0].Contents.OfType<FunctionResultContent>().First();
        var text = frc.Result as string;
        text.ShouldNotBeNull();
        text!.Length.ShouldBeLessThan(largeText.Length);
        text.ShouldStartWith(new string('x', 2_000));
        text.ShouldContain("[truncated: original 50,000 chars]");
    }

    [Fact]
    public void TruncateLargeToolResults_SmallResult_IsNotModified()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("call_1", "small result")])
        };

        var result = HistoryMiddleware.TruncateLargeToolResults(messages, maxChars: 30_000, previewChars: 2_000);

        result.ShouldBeSameAs(messages); // 同一引用（未修改）
    }

    [Fact]
    public void TruncateLargeToolResults_MixedResults_OnlyTruncatesLarge()
    {
        var largeText = new string('y', 40_000);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("call_1", largeText)]),
            new(ChatRole.Tool, [new FunctionResultContent("call_2", "small result")])
        };

        var result = HistoryMiddleware.TruncateLargeToolResults(messages, maxChars: 30_000, previewChars: 2_000);

        result.ShouldNotBeSameAs(messages);

        // 大结果被截断
        var frc1 = result[0].Contents.OfType<FunctionResultContent>().First();
        (frc1.Result as string)!.ShouldContain("[truncated:");

        // 小结果保持不变
        var frc2 = result[1].Contents.OfType<FunctionResultContent>().First();
        frc2.Result.ShouldBe("small result");
    }

    [Fact]
    public void TruncateLargeToolResults_OriginalMessagesNotMutated()
    {
        var largeText = new string('z', 50_000);
        var originalFrc = new FunctionResultContent("call_1", largeText);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [originalFrc])
        };

        _ = HistoryMiddleware.TruncateLargeToolResults(messages, maxChars: 30_000, previewChars: 2_000);

        // 原始消息未被修改（不可变性验证）
        var originalResult = messages[0].Contents.OfType<FunctionResultContent>().First();
        (originalResult.Result as string)!.Length.ShouldBe(50_000);
    }

    [Fact]
    public void TruncateLargeToolResults_EmptyMessages_ReturnsEmpty()
    {
        var messages = new List<ChatMessage>();

        var result = HistoryMiddleware.TruncateLargeToolResults(messages, maxChars: 30_000, previewChars: 2_000);

        result.ShouldBeSameAs(messages);
    }

    [Fact]
    public void TruncateLargeToolResults_NonToolMessage_NotAffected()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello world"),
            new(ChatRole.Assistant, "I can help you with that.")
        };

        var result = HistoryMiddleware.TruncateLargeToolResults(messages, maxChars: 100, previewChars: 50);

        result.ShouldBeSameAs(messages);
    }
}
