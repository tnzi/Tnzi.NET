using System.Text;

namespace Tnzi.AI.Tests.Providers;

/// <summary>
/// 五个 <see cref="IChatMessageProcessor"/> 此前只被注册、从未被调用：文档承诺的 Kimi / GLM / MiniMax
/// <c>&lt;think&gt;</c> 标签「自动处理」在生产路径根本不存在，推理草稿原样进入回复、被存进历史、下一轮回灌。
/// <see cref="MessageProcessingChatClient"/> 由 <see cref="ChatClientFactory"/> 按提供商形态包在 SDK 客户端外面。
/// </summary>
public class MessageProcessingChatClientTests
{
    private static ChatOptions NoOptions => new();

    private sealed class ScriptedChatClient(string responseText, params string[] streamChunks) : IChatClient
    {
        public IReadOnlyList<ChatMessage>? ReceivedMessages { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            ReceivedMessages = messages.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ReceivedMessages = messages.ToList();
            foreach (var chunk in streamChunks)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task GlmProvider_StripsThinkTagsFromResponseAndPopulatesReasoning()
    {
        var inner = new ScriptedChatClient("<think>plan the answer</think>The answer is 42.");
        var client = new MessageProcessingChatClient(inner, new GlmChatMessageProcessor());

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "?")], NoOptions);

        response.Text.ShouldBe("The answer is 42.");
        response.Messages.Single().Contents.OfType<TextReasoningContent>().Single().Text.ShouldBe("plan the answer");
    }

    [Fact]
    public async Task GlmProvider_Streaming_SplitsThinkSpanIntoReasoningEvenAcrossChunkBoundaries()
    {
        // 标签可以被切在任意位置：<thi | nk>pl | an</thi | nk>ans | wer
        var inner = new ScriptedChatClient("", "<thi", "nk>pl", "an</thi", "nk>ans", "wer");
        var client = new MessageProcessingChatClient(inner, new KimiChatMessageProcessor());

        var text = new StringBuilder();
        var reasoning = new StringBuilder();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "?")], NoOptions))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent r: reasoning.Append(r.Text); break;
                    case TextContent t: text.Append(t.Text); break;
                }
            }
        }

        text.ToString().ShouldBe("answer");
        reasoning.ToString().ShouldBe("plan");
    }

    [Fact]
    public async Task Streaming_WithoutThinkTags_PassesTextThroughUnchanged()
    {
        var inner = new ScriptedChatClient("", "Hello", ", ", "<b>bold</b>", " world");
        var client = new MessageProcessingChatClient(inner, new MiniMaxChatMessageProcessor());

        var text = new StringBuilder();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "?")], NoOptions))
        {
            text.Append(update.Text);
        }

        text.ToString().ShouldBe("Hello, <b>bold</b> world");
    }

    [Fact]
    public async Task KimiProvider_StripsThinkTagsFromOutgoingHistory()
    {
        var inner = new ScriptedChatClient("ok");
        var client = new MessageProcessingChatClient(inner, new KimiChatMessageProcessor());

        await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.User, "hi"),
            new ChatMessage(ChatRole.Assistant, "<think>secret draft</think>hello"),
            new ChatMessage(ChatRole.User, "again")
        ], NoOptions);

        inner.ReceivedMessages!.Select(m => m.Text).ShouldBe(["hi", "hello", "again"]);
    }

    [Theory]
    [InlineData("MyGlm", "OpenAI", "https://open.bigmodel.cn/api/paas/v4", "glm-z1-flash", "glm")]
    [InlineData("Kimi", "OpenAI", "https://api.moonshot.cn/v1", "kimi-k2", "kimi")]
    [InlineData("proxy", "OpenAI", "https://proxy.example/v1", "MiniMax-M1", "minimax")]
    [InlineData("deepseek", "OpenAI", "https://api.deepseek.com", "deepseek-chat", "deepseek")]
    [InlineData("OpenAI", "OpenAI", "https://api.openai.com/v1", "gpt-4o", null)]
    [InlineData("Anthropic", "Anthropic", null, "claude-sonnet-4", null)]
    public void Selector_MatchesByNameTypeHostOrModel(string name, string type, string? baseUrl, string model, string? expected)
    {
        IChatMessageProcessor[] processors =
        [
            new DeepSeekChatMessageProcessor(), new GeminiChatMessageProcessor(),
            new GlmChatMessageProcessor(), new KimiChatMessageProcessor(), new MiniMaxChatMessageProcessor()
        ];
        var options = new ProviderOptions { Name = name, ProviderType = type, BaseUrl = baseUrl };

        var selected = ChatMessageProcessorSelector.Select(processors, options, model);

        selected?.ProviderName.ShouldBe(expected);
        if (expected == null) selected.ShouldBeNull();
    }
}
