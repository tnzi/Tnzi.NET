namespace Tnzi.AI.Tests.Providers;

/// <summary>
/// IChatMessageProcessor 实现测试 — DeepSeek/Gemini/MiniMax 消息处理
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
}
