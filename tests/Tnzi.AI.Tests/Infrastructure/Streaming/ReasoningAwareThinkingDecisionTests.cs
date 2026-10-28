using System.Net;
using System.Text;

namespace Tnzi.AI.Tests.Infrastructure.Streaming;

/// <summary>
/// 推理参数由谁决定：管线内由 <see cref="ThinkingMiddleware"/> 决定（含显式关闭），管线外（IAiUtility、评审、摘要）
/// 才回退到提供商配置；无论哪条路，模型不支持推理就不注入，注入格式按装饰器绑定的提供商与模型识别。
/// </summary>
/// <remarks>
/// 此前装饰器只要看不到上下文就按提供商配置注入：中间件因请求要求 None、或模型不支持推理而刻意不设上下文时照样注入，
/// 且不带提供商身份，Gemini / Qwen 条目也被写成 OpenAI 的形态。
/// </remarks>
public class ReasoningAwareThinkingDecisionTests : IDisposable
{
    public ReasoningAwareThinkingDecisionTests()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    public void Dispose()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    private static ProviderOptions Provider(string name, ReasoningEffort effort, string? type = "OpenAI", string? baseUrl = null) => new()
    {
        Name = name,
        ProviderType = type,
        BaseUrl = baseUrl,
        ApiKey = "sk-test",
        Thinking = new ThinkingOptions { Effort = effort }
    };

    [Fact]
    public async Task OutsideThePipeline_NonReasoningModel_GetsNoThinking()
    {
        var inner = new ContextRecordingChatClient();
        var client = new ReasoningAwareChatClientDecorator(inner, Provider("OpenAI", ReasoningEffort.High), "gpt-4.1-mini");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        inner.Seen.ShouldHaveSingleItem().ShouldNotBeNull().Thinking.Effort.ShouldBe(ReasoningEffort.None);
    }

    [Fact]
    public async Task OutsideThePipeline_ReasoningModel_UsesTheProviderConfigInItsOwnFormat()
    {
        var inner = new ContextRecordingChatClient();
        var client = new ReasoningAwareChatClientDecorator(inner, Provider("gemini-prod", ReasoningEffort.High, type: "Gemini"), "gemini-2.5-flash");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var seen = inner.Seen.ShouldHaveSingleItem().ShouldNotBeNull();
        seen.Thinking.Effort.ShouldBe(ReasoningEffort.High);
        seen.Format.ShouldBe(ThinkingPayloadFormat.Gemini);
    }

    [Fact]
    public async Task PipelineTurnedThinkingOff_ProviderConfigIsNotAppliedBehindItsBack()
    {
        // 请求显式 ReasoningEffort=None：中间件写下「关」，装饰器不得再按提供商配置打开
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.None },
            ProviderName = "OpenAI"
        };
        var inner = new ContextRecordingChatClient();
        var client = new ReasoningAwareChatClientDecorator(inner, Provider("OpenAI", ReasoningEffort.High), "o4-mini");

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
        }

        inner.Seen.ShouldHaveSingleItem().ShouldNotBeNull().Thinking.Effort.ShouldBe(ReasoningEffort.None);
    }

    [Fact]
    public async Task PipelineTurnedThinkingOn_TheBoundProviderDecidesTheFormat()
    {
        // 中间件只知道条目名；降级链里每个客户端按自己的提供商出格式
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.Medium },
            ProviderName = "primary"
        };
        var inner = new ContextRecordingChatClient();
        var client = new ReasoningAwareChatClientDecorator(
            inner, Provider("tongyi", ReasoningEffort.None, baseUrl: "https://dashscope.aliyuncs.com/compatible-mode/v1"), "qwen3-max");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var seen = inner.Seen.ShouldHaveSingleItem().ShouldNotBeNull();
        seen.Thinking.Effort.ShouldBe(ReasoningEffort.Medium);
        seen.Format.ShouldBe(ThinkingPayloadFormat.Qwen);
    }

    [Fact]
    public async Task PipelineTurnedThinkingOn_FallbackToANonReasoningModel_GetsNoThinking()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High },
            ProviderName = "primary"
        };
        var inner = new ContextRecordingChatClient();
        var client = new ReasoningAwareChatClientDecorator(inner, Provider("fallback", ReasoningEffort.None), "gpt-4.1-mini");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        inner.Seen.ShouldHaveSingleItem().ShouldNotBeNull().Thinking.Effort.ShouldBe(ReasoningEffort.None);
    }

    [Theory]
    [InlineData("o4-mini", "high")]
    [InlineData("gpt-4.1-mini", null)]
    public async Task OpenAIWire_ProviderLevelThinking_ReachesTheBodyOnlyForReasoningModels(string model, string? expectedEffort)
    {
        var handler = new CapturingChatCompletionsHandler();
        var provider = new OpenAIChatClientProvider(new SingleClientFactory(handler), NullLogger<OpenAIChatClientProvider>.Instance);
        var client = provider.CreateChatClient(Provider("OpenAI", ReasoningEffort.High), model);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        using var body = JsonDocument.Parse(handler.LastBody.ShouldNotBeNull());
        body.RootElement.TryGetProperty("reasoning", out _).ShouldBeFalse();
        if (expectedEffort == null)
        {
            body.RootElement.TryGetProperty("reasoning_effort", out _).ShouldBeFalse();
        }
        else
        {
            body.RootElement.GetProperty("reasoning_effort").GetString().ShouldBe(expectedEffort);
        }
    }

    [Fact]
    public async Task ThinkingMiddleware_RequestEffortNone_ReachesTheDecoratorAsExplicitOff()
    {
        var options = new AIOptions();
        options.Providers["OpenAI"] = Provider("OpenAI", ReasoningEffort.High);
        var middleware = new ThinkingMiddleware(new StaticOptionsMonitor(options), NullLogger<ThinkingMiddleware>.Instance);
        var inner = new ContextRecordingChatClient();
        var client = new ReasoningAwareChatClientDecorator(inner, options.Providers["OpenAI"], "o4-mini");
        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "hi", ReasoningEffort = ReasoningEffort.None },
            Agent = AgentResolution.Success(agent: null!, provider: "OpenAI", model: "o4-mini", agentId: null),
            ServiceProvider = Mock.Of<IServiceProvider>()
        };

        await middleware.InvokeAsync(context, async (_, ct) =>
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: ct);
            return new AgentRunResult { Response = "ok" };
        });

        inner.Seen.ShouldHaveSingleItem().ShouldNotBeNull().Thinking.Effort.ShouldBe(ReasoningEffort.None);
    }

    private sealed class ContextRecordingChatClient : IChatClient
    {
        public List<ThinkingRequestContext?> Seen { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Seen.Add(ThinkingRequestPolicy.RequestContext.Value);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Seen.Add(ThinkingRequestPolicy.RequestContext.Value);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class CapturingChatCompletionsHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            const string response = """
                {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"o4-mini",
                 "choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],
                 "usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StaticOptionsMonitor(AIOptions value) : IOptionsMonitor<AIOptions>
    {
        public AIOptions CurrentValue => value;
        public AIOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AIOptions, string?> listener) => null;
    }
}
