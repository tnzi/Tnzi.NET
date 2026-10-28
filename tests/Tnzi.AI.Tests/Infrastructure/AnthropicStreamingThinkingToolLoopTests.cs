using System.Net;
using System.Text;
using Anthropic;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// 流式工具循环 + Anthropic extended thinking：第二轮请求里，上一轮带工具调用的助手消息必须以
/// 带签名的 thinking 块开头（API 要求：thinking 开启时，最后一组 tool_use / tool_result 之前的助手消息
/// 必须以 thinking / redacted_thinking 开头），否则请求被拒绝。
/// </summary>
/// <remarks>
/// 推理上下文跨 yield 可见之后（第二轮起照样开 thinking），流式执行器若仍把推理内容一律当合成块丢掉，
/// 每一次「先想再调工具」的流式运行都会在第二轮失败。走真实的 Anthropic SDK 适配器与真实的 SSE 解析。
/// </remarks>
public class AnthropicStreamingThinkingToolLoopTests : IDisposable
{
    private const string Signature = "sig-round-1";

    public AnthropicStreamingThinkingToolLoopTests()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    public void Dispose()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    [Fact]
    public async Task StreamingToolLoop_WithThinking_SendsTheSignedThinkingBlockBackBeforeTheToolUse()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High },
            ProviderName = "Anthropic"
        };
        var handler = new ScriptedSseHandler(FirstRound, SecondRound);
        var anthropic = new AnthropicClient { ApiKey = "sk-ant-test", HttpClient = new HttpClient(handler) };
        var client = AnthropicChatClientProvider.CreateThinkingAwareClient(anthropic, AnthropicWireHarness.Model);
        var tool = AIFunctionFactory.Create(() => "sunny", "get_weather", "Get the weather");
        var executor = new AgentExecutor(client, new AgentExecutorOptions { Tools = [tool] });

        var text = new StringBuilder();
        var reasoning = new StringBuilder();
        await foreach (var chunk in executor.ExecuteStreamingAsync([new ChatMessage(ChatRole.User, "Weather?")]))
        {
            text.Append(chunk.Text);
            reasoning.Append(chunk.ReasoningText);
        }

        text.ToString().ShouldBe("It is sunny.");
        reasoning.ToString().ShouldBe("Let me look up the weather.");
        handler.RequestBodies.Count.ShouldBe(2);

        using var second = JsonDocument.Parse(handler.RequestBodies[1]);
        var assistant = second.RootElement.GetProperty("messages").EnumerateArray()
            .Single(m => m.GetProperty("role").GetString() == "assistant");
        var blocks = assistant.GetProperty("content").EnumerateArray().ToList();
        blocks[0].GetProperty("type").GetString().ShouldBe("thinking");
        blocks[0].GetProperty("thinking").GetString().ShouldBe("Let me look up the weather.");
        blocks[0].GetProperty("signature").GetString().ShouldBe(Signature);
        blocks.Count(b => b.GetProperty("type").GetString() == "thinking").ShouldBe(1);
        blocks.Last().GetProperty("type").GetString().ShouldBe("tool_use");
    }

    /// <summary>
    /// redacted_thinking 必须原样以 redacted_thinking 发回。它在流里是「空文本 + 不透明数据」，与 display 被省略的
    /// thinking 块同形；适配器只凭 RawRepresentation 与 AdditionalProperties 上的标记区分两者，历史重建时丢了它们，
    /// 这个块就会被当成签名对不上的 thinking 块发回，第二轮被 API 拒绝。
    /// </summary>
    [Fact]
    public async Task StreamingToolLoop_WithRedactedThinking_SendsItBackAsRedactedThinking()
    {
        var second = await RunToolLoopAsync(RedactedFirstRound);

        var blocks = AssistantBlocks(second);
        blocks[0].GetProperty("type").GetString().ShouldBe("redacted_thinking");
        blocks[0].GetProperty("data").GetString().ShouldBe("opaque-redacted-data");
        blocks.ShouldNotContain(b => b.GetProperty("type").GetString() == "thinking");
        blocks.Last().GetProperty("type").GetString().ShouldBe("tool_use");
    }

    /// <summary>
    /// adaptive 型号默认 display=omitted：thinking 块只有签名、没有文本。它仍是 thinking 块，不是 redacted_thinking。
    /// </summary>
    [Fact]
    public async Task StreamingToolLoop_WithOmittedThinkingText_SendsTheSignedThinkingBlockBack()
    {
        var second = await RunToolLoopAsync(OmittedThinkingFirstRound, "claude-opus-5-5");

        var blocks = AssistantBlocks(second);
        blocks[0].GetProperty("type").GetString().ShouldBe("thinking");
        blocks[0].GetProperty("thinking").GetString().ShouldBe(string.Empty);
        blocks[0].GetProperty("signature").GetString().ShouldBe(Signature);
        blocks.ShouldNotContain(b => b.GetProperty("type").GetString() == "redacted_thinking");
    }

    private static async Task<string> RunToolLoopAsync(string firstRound, string model = AnthropicWireHarness.Model)
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High },
            ProviderName = "Anthropic"
        };
        var handler = new ScriptedSseHandler(firstRound, SecondRound);
        var anthropic = new AnthropicClient { ApiKey = "sk-ant-test", HttpClient = new HttpClient(handler) };
        var client = AnthropicChatClientProvider.CreateThinkingAwareClient(anthropic, model);
        var tool = AIFunctionFactory.Create(() => "sunny", "get_weather", "Get the weather");
        var executor = new AgentExecutor(client, new AgentExecutorOptions { Tools = [tool] });

        await foreach (var _ in executor.ExecuteStreamingAsync([new ChatMessage(ChatRole.User, "Weather?")]))
        {
        }

        handler.RequestBodies.Count.ShouldBe(2);
        return handler.RequestBodies[1];
    }

    private static List<JsonElement> AssistantBlocks(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        var assistant = doc.RootElement.GetProperty("messages").EnumerateArray()
            .Single(m => m.GetProperty("role").GetString() == "assistant");
        return assistant.GetProperty("content").EnumerateArray().Select(b => b.Clone()).ToList();
    }

    [Fact]
    public async Task StreamingToolLoop_WithoutSignedReasoning_KeepsReasoningOutOfTheHistory()
    {
        // 合成的推理块（OpenAI 兼容端点的 reasoning_content、<think> 切分）没有签名，提供商也不收回：照旧不进历史
        var inner = new SyntheticReasoningChatClient();
        var tool = AIFunctionFactory.Create(() => "sunny", "get_weather", "Get the weather");
        var executor = new AgentExecutor(inner, new AgentExecutorOptions { Tools = [tool] });

        await foreach (var _ in executor.ExecuteStreamingAsync([new ChatMessage(ChatRole.User, "Weather?")]))
        {
        }

        var assistant = inner.SecondRoundMessages!.Single(m => m.Role == ChatRole.Assistant);
        assistant.Contents.OfType<TextReasoningContent>().ShouldBeEmpty();
        assistant.Contents.OfType<FunctionCallContent>().ShouldHaveSingleItem();
    }

    private static string Sse(params (string Event, string Data)[] events)
        => string.Concat(events.Select(e => $"event: {e.Event}\ndata: {e.Data}\n\n"));

    private static readonly string FirstRound = Sse(
        ("message_start", """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-4-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}"""),
        ("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}"""),
        ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Let me look "}}"""),
        ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"up the weather."}}"""),
        ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig-round-1"}}"""),
        ("content_block_stop", """{"type":"content_block_stop","index":0}"""),
        ("content_block_start", """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_1","name":"get_weather","input":{}}}"""),
        ("content_block_delta", """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{}"}}"""),
        ("content_block_stop", """{"type":"content_block_stop","index":1}"""),
        ("message_delta", """{"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":20}}"""),
        ("message_stop", """{"type":"message_stop"}"""));

    private static readonly string RedactedFirstRound = Sse(
        ("message_start", """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-4-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}"""),
        ("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"redacted_thinking","data":"opaque-redacted-data"}}"""),
        ("content_block_stop", """{"type":"content_block_stop","index":0}"""),
        ("content_block_start", """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_1","name":"get_weather","input":{}}}"""),
        ("content_block_delta", """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{}"}}"""),
        ("content_block_stop", """{"type":"content_block_stop","index":1}"""),
        ("message_delta", """{"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":20}}"""),
        ("message_stop", """{"type":"message_stop"}"""));

    private static readonly string OmittedThinkingFirstRound = Sse(
        ("message_start", """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}"""),
        ("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}"""),
        ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig-round-1"}}"""),
        ("content_block_stop", """{"type":"content_block_stop","index":0}"""),
        ("content_block_start", """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_1","name":"get_weather","input":{}}}"""),
        ("content_block_delta", """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{}"}}"""),
        ("content_block_stop", """{"type":"content_block_stop","index":1}"""),
        ("message_delta", """{"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":20}}"""),
        ("message_stop", """{"type":"message_stop"}"""));

    private static readonly string SecondRound = Sse(
        ("message_start", """{"type":"message_start","message":{"id":"msg_2","type":"message","role":"assistant","model":"claude-sonnet-4-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":30,"output_tokens":1}}}"""),
        ("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""),
        ("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"It is sunny."}}"""),
        ("content_block_stop", """{"type":"content_block_stop","index":0}"""),
        ("message_delta", """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":5}}"""),
        ("message_stop", """{"type":"message_stop"}"""));

    private sealed class ScriptedSseHandler(params string[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            var body = responses[Math.Min(_next++, responses.Length - 1)];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            };
        }
    }

    private sealed class SyntheticReasoningChatClient : IChatClient
    {
        private int _calls;

        public IReadOnlyList<ChatMessage>? SecondRoundMessages { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_calls++ == 0)
            {
                yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextReasoningContent("thinking aloud")] };
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_weather", new Dictionary<string, object?>())]);
                yield break;
            }

            SecondRoundMessages = messages.ToList();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "It is sunny.") { FinishReason = ChatFinishReason.Stop };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
