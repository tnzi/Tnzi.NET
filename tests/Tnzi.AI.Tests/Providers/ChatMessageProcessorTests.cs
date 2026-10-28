namespace Tnzi.AI.Tests.Providers;

/// <summary>
/// IChatMessageProcessor 实现测试 - DeepSeek/Gemini/MiniMax 消息处理
/// </summary>
public class ChatMessageProcessorTests
{
    // -------------------------------------------------------------------------
    // DeepSeek
    // -------------------------------------------------------------------------

    [Fact]
    public void DeepSeek_ProviderName_IsCorrect()
    {
        var processor = new DeepSeekChatMessageProcessor();
        processor.ProviderName.ShouldBe("deepseek");
    }

    [Fact]
    public void DeepSeek_ProcessOutgoing_PreservesMessages()
    {
        var processor = new DeepSeekChatMessageProcessor();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there")
        };

        var result = processor.ProcessOutgoing(messages);

        result.Count.ShouldBe(2);
    }

    [Fact]
    public void DeepSeek_ProcessIncoming_PreservesReasoningContent()
    {
        var processor = new DeepSeekChatMessageProcessor();
        var msg = new ChatMessage(ChatRole.Assistant, "Answer");
        msg.AdditionalProperties = new AdditionalPropertiesDictionary
        {
            ["reasoning_content"] = "Let me think..."
        };

        var result = processor.ProcessIncoming([msg]);

        result.Count.ShouldBe(1);
        result[0].AdditionalProperties.ShouldNotBeNull();
        result[0].AdditionalProperties!["reasoning_content"].ShouldBe("Let me think...");
    }

    // -------------------------------------------------------------------------
    // Gemini
    // -------------------------------------------------------------------------

    [Fact]
    public void Gemini_ProviderName_IsCorrect()
    {
        var processor = new GeminiChatMessageProcessor();
        processor.ProviderName.ShouldBe("gemini");
    }

    [Fact]
    public void Gemini_ProcessIncoming_PreservesThoughtSignature()
    {
        var processor = new GeminiChatMessageProcessor();
        var msg = new ChatMessage(ChatRole.Assistant, "Answer");
        msg.AdditionalProperties = new AdditionalPropertiesDictionary
        {
            ["thought_signature"] = "sig123"
        };

        var result = processor.ProcessIncoming([msg]);

        result.Count.ShouldBe(1);
        result[0].AdditionalProperties!["thought_signature"].ShouldBe("sig123");
    }

    // -------------------------------------------------------------------------
    // MiniMax
    // -------------------------------------------------------------------------

    [Fact]
    public void MiniMax_ProviderName_IsCorrect()
    {
        var processor = new MiniMaxChatMessageProcessor();
        processor.ProviderName.ShouldBe("minimax");
    }

    [Fact]
    public void MiniMax_ProcessIncoming_ExtractsThinkTags()
    {
        var processor = new MiniMaxChatMessageProcessor();
        var msg = new ChatMessage(ChatRole.Assistant, "<think>reasoning here</think>The actual answer");

        var result = processor.ProcessIncoming([msg]);

        result.Count.ShouldBe(1);
        result[0].Text.ShouldBe("The actual answer");
        result[0].AdditionalProperties.ShouldNotBeNull();
        result[0].AdditionalProperties!["reasoning_content"].ShouldBe("reasoning here");
    }

    [Fact]
    public void MiniMax_ProcessOutgoing_StripsThinkTags()
    {
        var processor = new MiniMaxChatMessageProcessor();
        var msg = new ChatMessage(ChatRole.Assistant, "<think>old reasoning</think>old answer");

        var result = processor.ProcessOutgoing([msg]);

        result.Count.ShouldBe(1);
        result[0].Text.ShouldBe("old answer");
    }

    [Fact]
    public void MiniMax_ProcessIncoming_NoThinkTag_PassesThrough()
    {
        var processor = new MiniMaxChatMessageProcessor();
        var msg = new ChatMessage(ChatRole.Assistant, "Normal response");

        var result = processor.ProcessIncoming([msg]);

        result.Count.ShouldBe(1);
        result[0].Text.ShouldBe("Normal response");
    }

    [Fact]
    public void MiniMax_ProcessOutgoing_UserMessage_Unchanged()
    {
        var processor = new MiniMaxChatMessageProcessor();
        var msg = new ChatMessage(ChatRole.User, "<think>not a think tag for user</think>");

        var result = processor.ProcessOutgoing([msg]);

        result.Count.ShouldBe(1);
        result[0].Text.ShouldBe("<think>not a think tag for user</think>");
    }

    // -------------------------------------------------------------------------
    // 带 <think> 的助手消息里除正文以外的内容（工具调用等）必须原样保留：
    // 非流式执行器从 response.Messages 里找工具调用，丢了它运行就安静地结束
    // -------------------------------------------------------------------------

    [Fact]
    public void MiniMax_ProcessIncoming_ThinkTextWithToolCall_KeepsTheToolCallAndMessageIdentity()
    {
        var processor = new MiniMaxChatMessageProcessor();
        var call = new FunctionCallContent("call-1", "get_weather", new Dictionary<string, object?> { ["city"] = "Paris" });
        var msg = new ChatMessage(ChatRole.Assistant, [new TextContent("<think>need the weather</think>I will check."), call])
        {
            MessageId = "msg-1",
            AuthorName = "agent"
        };

        var result = processor.ProcessIncoming([msg]).Single();

        var keptCall = result.Contents.OfType<FunctionCallContent>().Single();
        keptCall.ShouldBeSameAs(call);
        result.Text.ShouldBe("I will check.");
        result.Contents.OfType<TextReasoningContent>().Single().Text.ShouldBe("need the weather");
        result.MessageId.ShouldBe("msg-1");
        result.AuthorName.ShouldBe("agent");
    }

    [Fact]
    public void Kimi_ProcessIncoming_ThinkOnlyTextWithToolCall_DropsTheEmptyTextButKeepsTheCall()
    {
        var processor = new KimiChatMessageProcessor();
        var call = new FunctionCallContent("call-1", "search", new Dictionary<string, object?>());
        var usage = new UsageContent(new UsageDetails { InputTokenCount = 10 });
        var msg = new ChatMessage(ChatRole.Assistant, [new TextContent("<think>search first</think>"), call, usage]);

        var result = processor.ProcessIncoming([msg]).Single();

        result.Contents.OfType<TextContent>().ShouldBeEmpty();
        result.Contents.OfType<FunctionCallContent>().Single().ShouldBeSameAs(call);
        result.Contents.OfType<UsageContent>().Single().ShouldBeSameAs(usage);
    }

    [Fact]
    public void Glm_ProcessOutgoing_HistoryWithThinkTextAndToolCall_KeepsTheToolCall()
    {
        var processor = new GlmChatMessageProcessor();
        var call = new FunctionCallContent("call-1", "lookup", new Dictionary<string, object?>());
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "hi"),
            new(ChatRole.Assistant, [new TextContent("<think>old draft</think>Looking it up."), call]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "result")])
        };

        var result = processor.ProcessOutgoing(history);

        result[1].Text.ShouldBe("Looking it up.");
        result[1].Contents.OfType<FunctionCallContent>().Single().ShouldBeSameAs(call);
        result[2].ShouldBeSameAs(history[2]);
    }

    [Fact]
    public void MiniMax_ProcessIncoming_ThinkTagSplitAcrossTextContents_IsStillExtracted()
    {
        var processor = new MiniMaxChatMessageProcessor();
        var msg = new ChatMessage(ChatRole.Assistant, [new TextContent("<think>part one "), new TextContent("part two</think>answer")]);

        var result = processor.ProcessIncoming([msg]).Single();

        result.Text.ShouldBe("answer");
        result.Contents.OfType<TextReasoningContent>().Single().Text.ShouldBe("part one part two");
    }
}
