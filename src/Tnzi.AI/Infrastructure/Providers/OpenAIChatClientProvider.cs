
namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// OpenAI 兼容的 ChatClient 提供商 - 覆盖 OpenAI / AzureOpenAI / Compatible 三种模式
/// </summary>
public class OpenAIChatClientProvider : IChatClientProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAIChatClientProvider> _logger;

    /// <summary>
    /// 提供商名称（通配，匹配所有 OpenAI 兼容端点）
    /// </summary>
    public string ProviderName => "OpenAI";

    public OpenAIChatClientProvider(IHttpClientFactory httpClientFactory, ILogger<OpenAIChatClientProvider> logger)
    {
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 创建 IChatClient
    /// </summary>
    public IChatClient CreateChatClient(ProviderOptions options, string model)
    {
        Check.NotNull(options);
        Check.NotNullOrWhiteSpace(model);

        var openAIClient = CreateOpenAIClient(options);
        var client = openAIClient.GetChatClient(model).AsIChatClient();
        return new ReasoningAwareChatClientDecorator(client, options.Thinking);
    }

    /// <summary>
    /// 创建 IEmbeddingGenerator
    /// </summary>
    public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(ProviderOptions options, string model)
    {
        Check.NotNull(options);
        Check.NotNullOrWhiteSpace(model);

        var openAIClient = CreateOpenAIClient(options);
        return openAIClient.GetEmbeddingClient(model).AsIEmbeddingGenerator();
    }

    /// <summary>
    /// 创建底层 OpenAI SDK 客户端
    /// </summary>
    public object CreateNativeClient(ProviderOptions options)
    {
        return CreateOpenAIClient(options);
    }

    /// <summary>
    /// 创建 OpenAI SDK Client
    /// </summary>
    internal OpenAIClient CreateOpenAIClient(ProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "ApiKey is required. Please configure ApiKey in provider options or set the corresponding environment variable.");
        }

        try
        {
            var credential = new ApiKeyCredential(options.ApiKey);
            var clientOptions = new OpenAIClientOptions();

            if (!string.IsNullOrWhiteSpace(options.BaseUrl)
                && Uri.TryCreate(options.BaseUrl.TrimEnd('/'), UriKind.Absolute, out var endpoint)
                && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps))
            {
                clientOptions.Endpoint = endpoint;
            }

            var httpClient = _httpClientFactory.CreateClient(options.HttpClientName ?? ResilientHttpClientNames.For(options.Name));
            if (options.TimeoutSeconds.HasValue)
            {
                var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds.Value);
                httpClient.Timeout = timeout;
                // SDK 管线自带每次尝试 100 秒的网络超时，不跟着改的话 TimeoutSeconds 超过 100 会被它静默截断。
                clientOptions.NetworkTimeout = timeout;
            }
            clientOptions.Transport = new HttpClientPipelineTransport(httpClient);

            // Add thinking request policy to inject provider-specific thinking params (e.g., Gemini extra_body)
            // PipelinePolicy runs inside the SDK's pipeline, guaranteed to intercept every request.
            clientOptions.AddPolicy(new ThinkingRequestPolicy(), PipelinePosition.BeforeTransport);

            return new OpenAIClient(credential, clientOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create OpenAI client");
            throw new InvalidOperationException($"Failed to create OpenAI client: {ex.Message}", ex);
        }
    }
}
