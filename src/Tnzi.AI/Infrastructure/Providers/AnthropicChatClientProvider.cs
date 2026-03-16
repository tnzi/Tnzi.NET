using Anthropic;

namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Anthropic 原生 ChatClient 提供商 — 通过官方 Anthropic SDK 直接对接 Claude API。
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
    /// 创建 IChatClient — 使用 Anthropic SDK 原生 IChatClient 适配器 + thinking 装饰器
    /// </summary>
    public IChatClient CreateChatClient(ProviderOptions options, string model)
    {
        Check.NotNull(options);
        Check.NotNullOrWhiteSpace(model);

        var client = CreateAnthropicClient(options);
        var chatClient = client.AsIChatClient(model);
        return new AnthropicThinkingChatClient(chatClient);
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
            var httpClient = _httpClientFactory.CreateClient("Tnzi.AI.Resilient");
            if (options.TimeoutSeconds.HasValue)
            {
                httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds.Value);
            }

            TimeSpan? timeout = options.TimeoutSeconds.HasValue
                ? TimeSpan.FromSeconds(options.TimeoutSeconds.Value)
                : null;

            var client = baseUrl != null
                ? new AnthropicClient
                {
                    ApiKey = apiKey,
                    HttpClient = httpClient,
                    BaseUrl = baseUrl,
                    Timeout = timeout
                }
                : new AnthropicClient
                {
                    ApiKey = apiKey,
                    HttpClient = httpClient,
                    Timeout = timeout
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
