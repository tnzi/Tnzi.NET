using System.Net;
using System.Text;

namespace Tnzi.AI.Tests;

/// <summary>
/// AiUtilityService 单元测试
/// </summary>
public class AiUtilityServiceTests
{
    private readonly Mock<IChatClientFactory> _mockFactory;
    private readonly Mock<IChatClient> _mockChatClient;
    private readonly Mock<IOptionsMonitor<AiUtilityOptions>> _mockOptions;

    public AiUtilityServiceTests()
    {
        _mockFactory = new Mock<IChatClientFactory>();
        _mockChatClient = new Mock<IChatClient>();
        _mockOptions = new Mock<IOptionsMonitor<AiUtilityOptions>>();

        _mockOptions.Setup(x => x.CurrentValue).Returns(new AiUtilityOptions
        {
            MaxTokens = 100,
            Temperature = 0.3
        });

        _mockFactory
            .Setup(x => x.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(_mockChatClient.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidInput_ReturnsResponse()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test Title")));

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object);

        var result = await service.ExecuteAsync("You are a title generator.", "Generate a title for my article.");

        result.ShouldBe("Test Title");
    }

    [Fact]
    public async Task ExecuteAsync_WithCallOptions_UsesModelOverride()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "response")));

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object);

        await service.ExecuteAsync(
            "System prompt",
            "User message",
            new AiUtilityCallOptions { Model = "gpt-4.1-mini" });

        _mockFactory.Verify(
            x => x.GetChatClient(null, "gpt-4.1-mini"),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenChatClientReturnsEmpty_ReturnsNull()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object);

        var result = await service.ExecuteAsync("System prompt", "User message");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionThrown_ReturnsNull()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object);

        var result = await service.ExecuteAsync("System prompt", "User message");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenCallerCancels_Rethrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object);

        await Should.ThrowAsync<OperationCanceledException>(
            () => service.ExecuteAsync("System prompt", "User message", cancellationToken: cts.Token));
    }

    /// <summary>
    /// 超时（HttpClient.Timeout、弹性管线的超时）抛的也是 <see cref="OperationCanceledException"/>，但调用方没有取消：
    /// 那是一次失败，契约是返回 null，与核心实现、自带提供商分支一致。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TimeoutWithoutCallerCancellation_ReturnsNull()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing."));

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object);

        var result = await service.ExecuteAsync("System prompt", "User message", cancellationToken: CancellationToken.None);

        result.ShouldBeNull();
    }

    // ------------------------------------------------------------------
    // 调用方自带提供商（AiUtilityCallOptions.Provider）
    // ------------------------------------------------------------------

    private const string InlineKey = "sk-inline-secret-0123456789";

    private static AiUtilityInlineProvider Inline(string baseUrl = "https://vendor.example.net/v1", string apiKey = InlineKey, string? defaultModel = "vendor-model")
        => new() { BaseUrl = baseUrl, ApiKey = apiKey, DefaultModel = defaultModel };

    [Fact]
    public async Task Inline_RealOpenAiProvider_SendsToSuppliedBaseUrlWithSuppliedKeyAndModel()
    {
        // 走真实的 OpenAIChatClientProvider + SDK，只把最底层的 HTTP 换成录制器：
        // 断言的是线上真正发出去的请求，而不是「某个 mock 被以某些参数调用过」。
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        // 自带提供商必须走核心的 Inline 客户端（无 HttpClient 超时、无 Polly 管线）。
        // 回退管线是配置提供商之外所有端点共用的熔断器，自带提供商不该与它们共用 —— 走到它就当场失败。
        services.AddHttpClient(AiUtilityHttpClientNames.Inline).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(ResilientHttpClientNames.Fallback)
            .ConfigurePrimaryHttpMessageHandler(() => new ThrowingHandler("inline provider must not use the resilient client"));
        var httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var openAi = new OpenAIChatClientProvider(httpClientFactory, NullLogger<OpenAIChatClientProvider>.Instance);

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object, chatClientProviders: [openAi]);

        var result = await service.ExecuteAsync("System prompt", "User message", new AiUtilityCallOptions { Provider = Inline() });

        result.ShouldBe("inline answer");
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.ToString().ShouldBe("https://vendor.example.net/v1/chat/completions");
        request.Authorization.ShouldBe("Bearer " + InlineKey);
        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.GetProperty("model").GetString().ShouldBe("vendor-model");
        _mockFactory.Verify(x => x.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Inline_CallModelOverridesInlineDefaultModel_AndUtilityDefaultsApply()
    {
        var (provider, captured) = RecordingProvider();
        ChatOptions? sentOptions = null;
        _mockChatClient
            .Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, o, _) => sentOptions = o)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object, chatClientProviders: [provider.Object]);

        await service.ExecuteAsync("System prompt", "User message", new AiUtilityCallOptions { Model = "vendor-large", Provider = Inline() });

        var (options, model) = captured.ShouldHaveSingleItem();
        model.ShouldBe("vendor-large");
        options.BaseUrl.ShouldBe("https://vendor.example.net/v1");
        options.ApiKey.ShouldBe(InlineKey);
        sentOptions!.MaxOutputTokens.ShouldBe(100);
    }

    [Fact]
    public async Task Inline_PassesTimeoutAndTheInlineClientName_ToTheProvider()
    {
        var (provider, captured) = RecordingProvider();
        _mockChatClient
            .Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object, chatClientProviders: [provider.Object]);
        var inline = new AiUtilityInlineProvider { BaseUrl = "https://vendor.example.net/v1", ApiKey = InlineKey, DefaultModel = "vendor-model", TimeoutSeconds = 300 };

        await service.ExecuteAsync("System prompt", "User message", new AiUtilityCallOptions { Provider = inline });

        var (options, _) = captured.ShouldHaveSingleItem();
        options.TimeoutSeconds.ShouldBe(300);
        options.HttpClientName.ShouldBe(AiUtilityHttpClientNames.Inline);
    }

    [Theory]
    [InlineData("", InlineKey, "vendor-model")]
    [InlineData("ftp://vendor.example.net/v1", InlineKey, "vendor-model")]
    [InlineData(InlineKey, InlineKey, "vendor-model")]
    [InlineData("https://vendor.example.net/v1", "  ", "vendor-model")]
    [InlineData("https://vendor.example.net/v1", InlineKey, null)]
    public async Task Inline_Invalid_ReturnsNullWithWarning_NoClientBuilt_KeyNotLogged(string baseUrl, string apiKey, string? defaultModel)
    {
        var (provider, captured) = RecordingProvider();
        var logger = new CapturingLogger();
        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object, logger, [provider.Object]);

        var result = await service.ExecuteAsync("System prompt", "User message",
            new AiUtilityCallOptions { Provider = Inline(baseUrl, apiKey, defaultModel) });

        result.ShouldBeNull();
        captured.ShouldBeEmpty();
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
        logger.AssertNeverContains(InlineKey);
    }

    [Fact]
    public async Task Inline_FailureMessageEchoingTheKey_IsRedactedInLogs()
    {
        var (provider, _) = RecordingProvider();
        _mockChatClient
            .Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"HTTP 401: Incorrect API key provided: {InlineKey}"));
        var logger = new CapturingLogger();
        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object, logger, [provider.Object]);

        var result = await service.ExecuteAsync("System prompt", "User message", new AiUtilityCallOptions { Provider = Inline() });

        result.ShouldBeNull();
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("inline:vendor.example.net") && e.Message.Contains("***"));
        logger.AssertNeverContains(InlineKey);
    }

    [Fact]
    public async Task Inline_NoOpenAiCompatibleProviderRegistered_ReturnsNullWithWarning()
    {
        var logger = new CapturingLogger();
        var service = new AiUtilityService(_mockFactory.Object, _mockOptions.Object, logger);

        var result = await service.ExecuteAsync("System prompt", "User message", new AiUtilityCallOptions { Provider = Inline() });

        result.ShouldBeNull();
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
        _mockFactory.Verify(x => x.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>名为 OpenAI 的 provider 替身：记下每次建客户端时的选项与模型，返回共用的 mock 客户端。</summary>
    private (Mock<IChatClientProvider> Provider, List<(ProviderOptions Options, string Model)> Captured) RecordingProvider()
    {
        var captured = new List<(ProviderOptions, string)>();
        var provider = new Mock<IChatClientProvider>();
        provider.SetupGet(p => p.ProviderName).Returns("OpenAI");
        provider
            .Setup(p => p.CreateChatClient(It.IsAny<ProviderOptions>(), It.IsAny<string>()))
            .Callback<ProviderOptions, string>((o, m) => captured.Add((o, m)))
            .Returns(_mockChatClient.Object);
        return (provider, captured);
    }

    private sealed record CapturedRequest(Uri Uri, string? Authorization, string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            const string completion = """
                {"id":"chatcmpl-1","object":"chat.completion","created":0,"model":"vendor-model",
                 "choices":[{"index":0,"message":{"role":"assistant","content":"inline answer"},"finish_reason":"stop"}]}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(completion, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHandler(string reason) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException(reason);
    }

    private sealed record LogEntry(LogLevel Level, string Message, string? Exception);

    /// <summary>记下每一条日志的渲染文本与异常全文 —— 断言密钥不在任何一处。</summary>
    private sealed class CapturingLogger : ILogger<AiUtilityService>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception?.ToString()));

        public void AssertNeverContains(string secret)
        {
            foreach (var entry in Entries)
            {
                entry.Message.ShouldNotContain(secret);
                (entry.Exception ?? string.Empty).ShouldNotContain(secret);
            }
        }
    }
}
