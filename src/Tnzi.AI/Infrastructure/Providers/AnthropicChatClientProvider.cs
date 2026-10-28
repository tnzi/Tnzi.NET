using Anthropic;

namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Anthropic 原生 ChatClient 提供商 - 通过官方 Anthropic SDK 直接对接 Claude API。
/// SDK 原生支持 TextReasoningContent（thinking/reasoning），无需兼容层。
/// </summary>
public class AnthropicChatClientProvider : IChatClientProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnthropicChatClientProvider> _logger;

    /// <summary>
    /// 提供商名称
    /// </summary>
    public string ProviderName => "Anthropic";

    public AnthropicChatClientProvider(IHttpClientFactory httpClientFactory, ILogger<AnthropicChatClientProvider> logger)
    {
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 创建 IChatClient - 使用 Anthropic SDK 原生 IChatClient 适配器 + thinking 装饰器
    /// </summary>
    public IChatClient CreateChatClient(ProviderOptions options, string model)
    {
        Check.NotNull(options);
        Check.NotNullOrWhiteSpace(model);

        return CreateThinkingAwareClient(CreateAnthropicClient(options), model);
    }

    /// <summary>
    /// SDK 适配器 + thinking 装饰器，按型号成对选定 thinking 的请求形态。
    /// </summary>
    /// <remarks>
    /// 适配器只能在创建时选一种形态（SDK 默认 adaptive），而 4.6 之前的型号拒收 adaptive、4.7 起的型号拒收
    /// <c>budget_tokens</c>，选错了一开推理就是 400。装饰器也要知道选了哪种：显式预算只在 extended 下发得出去。
    /// </remarks>
    internal static IChatClient CreateThinkingAwareClient(IAnthropicClient client, string model)
    {
        var adaptive = ModelCapabilities.UsesClaudeAdaptiveThinking(model);
        var chatClient = client.AsIChatClient(
            model,
            thinkingMode: adaptive ? AnthropicThinkingMode.Adaptive : AnthropicThinkingMode.Extended);
        return new AnthropicThinkingChatClient(chatClient, adaptive);
    }

    /// <summary>
    /// Anthropic 不提供嵌入模型，返回 null
    /// </summary>
    public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(ProviderOptions options, string model)
    {
        return null;
    }

    /// <summary>
    /// 创建底层 AnthropicClient 实例
    /// </summary>
    public object CreateNativeClient(ProviderOptions options)
    {
        return CreateAnthropicClient(options);
    }

    private AnthropicClient CreateAnthropicClient(ProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "ApiKey is required for Anthropic provider. Please configure ApiKey in provider options or set the AI__ANTHROPIC__APIKEY environment variable.");
        }

        var apiKey = options.ApiKey;
        var baseUrl = !string.IsNullOrWhiteSpace(options.BaseUrl) ? options.BaseUrl.TrimEnd('/') : null;

        try
        {
            var httpClient = _httpClientFactory.CreateClient(options.HttpClientName ?? ResilientHttpClientNames.For(options.Name));
            if (options.TimeoutSeconds.HasValue)
            {
                httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds.Value);
            }

            TimeSpan? timeout = options.TimeoutSeconds.HasValue
                ? TimeSpan.FromSeconds(options.TimeoutSeconds.Value)
                : null;

            // 重试只留一层：框架的命名客户端（按提供商名的、回退的、租户端点的）都挂着 Polly 标准管线（重试 3 次）。
            // SDK 自己默认再重试 2 次，而且连客户端超时也重试、无上限地遵从 Retry-After；它叠在 HttpClient 外面，
            // 一次调用最多变成 3 轮完整的 Polly 重试（12 次 HTTP 尝试），HttpClient.Timeout（= TimeoutSeconds）也只约束其中一轮。
            // 调用方另给的客户端名不保证有管线，照旧交给 SDK 重试（null = SDK 默认）。
            int? maxRetries = options.HttpClientName is null || options.HttpClientName == ResilientHttpClientNames.TenantEndpoint
                ? 0
                : null;
            var client = baseUrl != null
                ? new AnthropicClient
                {
                    ApiKey = apiKey,
                    HttpClient = httpClient,
                    BaseUrl = baseUrl,
                    Timeout = timeout,
                    MaxRetries = maxRetries
                }
                : new AnthropicClient
                {
                    ApiKey = apiKey,
                    HttpClient = httpClient,
                    Timeout = timeout,
                    MaxRetries = maxRetries
                };

            return client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Anthropic client");
            throw new InvalidOperationException($"Failed to create Anthropic client: {ex.Message}", ex);
        }
    }
}
