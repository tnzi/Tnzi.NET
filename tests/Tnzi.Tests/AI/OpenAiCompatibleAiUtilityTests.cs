using System.Net;
using Tnzi.AI.Options;

namespace Tnzi.Tests.AI;

/// <summary>
/// 核心 <see cref="IAiUtility"/> 默认实现的单元测试。
/// </summary>
/// <remarks>
/// 这些用例守着「不加载 Tnzi.AI 也能调 LLM」这条能力：请求形状（端点拼接、鉴权头、
/// 消息体）、配置解析（提供商 / 模型别名 / 覆盖优先级）、以及失败时的重试判据。
/// </remarks>
public class OpenAiCompatibleAiUtilityTests
{
    private const string ProviderName = "test";

    // ------------------------------------------------------------------
    // 配置缺失 —— 一律返回 null 且不发出任何请求
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoProvidersConfigured_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var sut = CreateSut(handler, new AiProviderRegistryOptions());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderDisabled_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var registry = Registry(p => p.Enabled = false);
        var sut = CreateSut(handler, registry);

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultProviderNotInDictionary_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var registry = Registry();
        registry.DefaultProvider = "missing";
        var sut = CreateSut(handler, registry);

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_NoApiKey_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var registry = Registry(p => p.ApiKey = null);
        var sut = CreateSut(handler, registry);

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_NoModelAnywhere_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var registry = Registry(p => p.DefaultModel = null);
        var sut = CreateSut(handler, registry, new AiUtilityOptions { Model = null });

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_NonHttpBaseUrl_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var registry = Registry(p => p.BaseUrl = "ftp://example.com/v1");
        var sut = CreateSut(handler, registry);

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    // ------------------------------------------------------------------
    // IsAvailable —— 调用方据此决定要不要把 AI 入口显示出来
    // ------------------------------------------------------------------

    [Fact]
    public void IsAvailable_EnabledProviderWithApiKey_IsTrue()
    {
        Assert.True(CreateSut(new RecordingHandler(), Registry()).IsAvailable);
    }

    [Fact]
    public void IsAvailable_NoProvidersConfigured_IsFalse()
    {
        Assert.False(CreateSut(new RecordingHandler(), new AiProviderRegistryOptions()).IsAvailable);
    }

    [Fact]
    public void IsAvailable_ProviderDisabled_IsFalse()
    {
        Assert.False(CreateSut(new RecordingHandler(), Registry(p => p.Enabled = false)).IsAvailable);
    }

    [Fact]
    public void IsAvailable_DefaultProviderNotInDictionary_IsFalse()
    {
        var registry = Registry();
        registry.DefaultProvider = "missing";

        Assert.False(CreateSut(new RecordingHandler(), registry).IsAvailable);
    }

    [Fact]
    public void IsAvailable_ProviderWithoutApiKey_IsFalse()
    {
        // 「已启用但没配 key」正是那种「按钮显示出来、点了没反应」的形态。
        Assert.False(CreateSut(new RecordingHandler(), Registry(p => p.ApiKey = null)).IsAvailable);
    }

    [Fact]
    public void IsAvailable_ReflectsOptionsChange_WithoutRecreatingTheService()
    {
        // 不缓存探测结果：配置热更新后必须立刻反映，否则 admin 改了配置还要重启才能看到入口。
        var registry = Registry();
        var monitor = new MutableOptionsMonitor<AiProviderRegistryOptions>(registry);
        var services = new ServiceCollection();
        services.AddHttpClient(AiUtilityHttpClientNames.Fallback);
        var sut = new OpenAiCompatibleAiUtility(
            services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(),
            monitor,
            new StaticOptionsMonitor<AiUtilityOptions>(new AiUtilityOptions()));

        Assert.True(sut.IsAvailable);

        monitor.Set(new AiProviderRegistryOptions());

        Assert.False(sut.IsAvailable);
    }

    // ------------------------------------------------------------------
    // 请求形状
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsTrimmedContent()
    {
        var handler = new RecordingHandler(Completion("  hello world  "));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task ExecuteAsync_PostsToChatCompletionsUnderConfiguredBaseUrl()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(p => p.BaseUrl = "https://api.deepseek.com/v1"));

        await sut.ExecuteAsync("system", "user");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.deepseek.com/v1/chat/completions", request.Uri.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_TrailingSlashInBaseUrl_DoesNotDoubleUpSeparator()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(p => p.BaseUrl = "https://api.deepseek.com/v1/"));

        await sut.ExecuteAsync("system", "user");

        Assert.Equal("https://api.deepseek.com/v1/chat/completions", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_NoBaseUrl_FallsBackToOfficialOpenAiEndpoint()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(p => p.BaseUrl = null));

        await sut.ExecuteAsync("system", "user");

        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_SendsBearerAuthorizationHeader()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(p => p.ApiKey = "sk-secret"));

        await sut.ExecuteAsync("system", "user");

        Assert.Equal("Bearer sk-secret", handler.Requests[0].Authorization);
    }

    [Fact]
    public async Task ExecuteAsync_SendsSystemThenUserMessage()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry());

        await sut.ExecuteAsync("you are a helper", "what is 2+2?");

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("you are a helper", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("what is 2+2?", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_SendsSnakeCaseMaxTokens()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(), new AiUtilityOptions { MaxTokens = 321, Temperature = 0.7 });

        await sut.ExecuteAsync("system", "user");

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(321, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.7, body.RootElement.GetProperty("temperature").GetDouble(), 3);
    }

    // ------------------------------------------------------------------
    // 模型与覆盖优先级
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_UsesProviderDefaultModel()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(p => p.DefaultModel = "gpt-4.1-mini"), new AiUtilityOptions { Model = null });

        await sut.ExecuteAsync("system", "user");

        Assert.Equal("gpt-4.1-mini", ModelOf(handler.Requests[0]));
    }

    [Fact]
    public async Task ExecuteAsync_CallOptionsModelOverridesUtilityDefault()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(), new AiUtilityOptions { Model = "from-options" });

        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Model = "from-call" });

        Assert.Equal("from-call", ModelOf(handler.Requests[0]));
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesModelAlias()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var registry = Registry(p => p.Models = new Dictionary<string, string> { ["fast"] = "gpt-4.1-mini" });
        var sut = CreateSut(handler, registry);

        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Model = "fast" });

        Assert.Equal("gpt-4.1-mini", ModelOf(handler.Requests[0]));
    }

    [Fact]
    public async Task ExecuteAsync_CallOptionsOverrideTokensAndTemperature()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(), new AiUtilityOptions { MaxTokens = 100, Temperature = 0.3 });

        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { MaxTokens = 7, Temperature = 1.5 });

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(7, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(1.5, body.RootElement.GetProperty("temperature").GetDouble(), 3);
    }

    // ------------------------------------------------------------------
    // 重试判据
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RateLimitedThenSuccess_RetriesAndReturnsContent()
    {
        var handler = new RecordingHandler(
            () => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            () => Completion("recovered"));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Equal("recovered", result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ServerErrorThenSuccess_Retries()
    {
        var handler = new RecordingHandler(
            () => new HttpResponseMessage(HttpStatusCode.BadGateway),
            () => Completion("recovered"));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Equal("recovered", result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ExecuteAsync_PersistentServerError_StopsAfterRetryBudget()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        // 首次尝试 + 2 次重试
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ClientError_DoesNotRetry()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_Unauthorized_DoesNotRetry()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_TransportFailureThenSuccess_Retries()
    {
        var attempts = 0;
        var handler = new RecordingHandler(() =>
        {
            attempts++;
            if (attempts == 1) throw new HttpRequestException("connection reset");
            return Completion("recovered");
        });
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Equal("recovered", result);
        Assert.Equal(2, attempts);
    }

    // ------------------------------------------------------------------
    // 响应解析
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ResponseWithoutChoices_ReturnsNull()
    {
        var handler = new RecordingHandler(Json("""{"id":"x","choices":[]}"""));
        var sut = CreateSut(handler, Registry());

        Assert.Null(await sut.ExecuteAsync("system", "user"));
    }

    [Fact]
    public async Task ExecuteAsync_ChoiceWithoutTextContent_ReturnsNull()
    {
        var handler = new RecordingHandler(Json("""{"choices":[{"message":{"reasoning_content":"thinking"}}]}"""));
        var sut = CreateSut(handler, Registry());

        Assert.Null(await sut.ExecuteAsync("system", "user"));
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceOnlyContent_ReturnsNull()
    {
        var handler = new RecordingHandler(Completion("   "));
        var sut = CreateSut(handler, Registry());

        Assert.Null(await sut.ExecuteAsync("system", "user"));
    }

    [Fact]
    public async Task ExecuteAsync_NonJsonResponse_ReturnsNull()
    {
        var handler = new RecordingHandler(Json("<html>gateway error</html>"));
        var sut = CreateSut(handler, Registry());

        Assert.Null(await sut.ExecuteAsync("system", "user"));
    }

    // ------------------------------------------------------------------
    // 取消
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var handler = new RecordingHandler(() =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var sut = CreateSut(handler, Registry());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.ExecuteAsync("system", "user", cancellationToken: cts.Token));
    }

    // ------------------------------------------------------------------
    // 参数校验
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_BlankSystemPrompt_Throws(string systemPrompt)
    {
        var sut = CreateSut(new RecordingHandler(), Registry());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => sut.ExecuteAsync(systemPrompt, "user"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_BlankUserMessage_Throws(string userMessage)
    {
        var sut = CreateSut(new RecordingHandler(), Registry());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => sut.ExecuteAsync("system", userMessage));
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static AiProviderRegistryOptions Registry(Action<AiProviderOptions>? configure = null)
    {
        var provider = new AiProviderOptions
        {
            Name = ProviderName,
            Enabled = true,
            ApiKey = "sk-test-key",
            BaseUrl = "https://api.example.com/v1",
            DefaultModel = "test-model"
        };
        configure?.Invoke(provider);

        return new AiProviderRegistryOptions
        {
            DefaultProvider = ProviderName,
            Providers = new Dictionary<string, AiProviderOptions> { [ProviderName] = provider }
        };
    }

    private static OpenAiCompatibleAiUtility CreateSut(
        RecordingHandler handler,
        AiProviderRegistryOptions registry,
        AiUtilityOptions? utility = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(AiUtilityHttpClientNames.For(ProviderName))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(AiUtilityHttpClientNames.For("missing"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(AiUtilityHttpClientNames.Fallback)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider();

        return new OpenAiCompatibleAiUtility(
            provider.GetRequiredService<IHttpClientFactory>(),
            new StaticOptionsMonitor<AiProviderRegistryOptions>(registry),
            new StaticOptionsMonitor<AiUtilityOptions>(utility ?? new AiUtilityOptions()));
    }

    private static string ModelOf(CapturedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body);
        return body.RootElement.GetProperty("model").GetString()!;
    }

    private static HttpResponseMessage Completion(string content)
        => Json(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content } } }
        }));

    private static HttpResponseMessage Json(string payload)
        => new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(Uri Uri, HttpMethod Method, string? Authorization, string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _queued = new();
        private readonly Func<HttpResponseMessage>? _repeating;

        public List<CapturedRequest> Requests { get; } = [];

        /// <summary>永远返回 200 空 completion（用于「不该发请求」的断言）。</summary>
        public RecordingHandler() => _repeating = () => Completion(string.Empty);

        /// <summary>固定响应，可被重复取用。</summary>
        public RecordingHandler(HttpResponseMessage response) => _repeating = () => Clone(response);

        /// <summary>每次调用都执行同一个工厂（可抛异常，用于传输层失败）。</summary>
        public RecordingHandler(Func<HttpResponseMessage> factory) => _repeating = factory;

        /// <summary>按顺序返回，用尽后重复最后一个。</summary>
        public RecordingHandler(params Func<HttpResponseMessage>[] sequence)
        {
            foreach (var item in sequence)
            {
                _queued.Enqueue(item);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.RequestUri!,
                request.Method,
                request.Headers.TryGetValues("Authorization", out var values) ? string.Join(' ', values) : null,
                body));

            var factory = _queued.Count > 0 ? _queued.Dequeue() : _repeating;
            return factory!();
        }

        private static HttpResponseMessage Clone(HttpResponseMessage source)
            => new(source.StatusCode)
            {
                Content = new StringContent(
                    source.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                    Encoding.UTF8,
                    "application/json")
            };
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class MutableOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; private set; } = value;

        public void Set(T next) => CurrentValue = next;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
