
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace Tnzi.AI.Infrastructure;

/// <summary>
/// ChatClient 工厂 - 创建不同提供商的 ChatClient 和 EmbeddingClient
/// </summary>
public class ChatClientFactory : IChatClientFactory
{
    private readonly IOptions<AIOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, ChatClient> _chatClients = new();
    private readonly ConcurrentDictionary<string, EmbeddingClient> _embeddingClients = new();
    private readonly ILogger<ChatClientFactory> _logger;

    public ChatClientFactory(
        IOptions<AIOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<ChatClientFactory> logger)
    {
        _options = Check.NotNull(options);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 获取 ChatClient
    /// </summary>
    public ChatClient GetChatClient(string? providerName = null, string? model = null)
    {
        var (name, providerOptions) = ResolveProvider(providerName);
        model = ResolveModel(name, providerOptions, model);

        // 缓存键包含 BaseUrl，确保同一提供商不同端点（如 staging/production）不会共享缓存
        var cacheKey = $"chat:{name}:{providerOptions.BaseUrl ?? "default"}:{model}";

        // 使用 GetOrAdd 确保线程安全的缓存操作
        return _chatClients.GetOrAdd(cacheKey, _ =>
        {
            var openAIClient = CreateOpenAIClient(name, providerOptions);
            return openAIClient.GetChatClient(model);
        });
    }

    /// <summary>
    /// 获取 EmbeddingClient
    /// </summary>
    public EmbeddingClient GetEmbeddingClient(string? providerName = null, string? model = null)
    {
        var (name, providerOptions) = ResolveProvider(providerName);
        model = ResolveModel(name, providerOptions, model);

        var cacheKey = $"emb:{name}:{providerOptions.BaseUrl ?? "default"}:{model}";

        return _embeddingClients.GetOrAdd(cacheKey, _ =>
        {
            var openAIClient = CreateOpenAIClient(name, providerOptions);
            return openAIClient.GetEmbeddingClient(model);
        });
    }

    /// <summary>
    /// 解析并验证提供商配置
    /// </summary>
    private (string name, ProviderOptions options) ResolveProvider(string? providerName)
    {
        providerName ??= _options.Value.DefaultProvider;

        // 先查找并验证提供商配置，避免将验证逻辑放入 GetOrAdd（防止异常被缓存为失败状态）
        if (!_options.Value.Providers.TryGetValue(providerName, out var providerOptions))
        {
            throw new InvalidOperationException($"Provider '{providerName}' not found in configuration");
        }

        if (!providerOptions.Enabled)
        {
            throw new InvalidOperationException($"Provider '{providerName}' is disabled");
        }

        return (providerName, providerOptions);
    }

    /// <summary>
    /// 解析模型名称，未指定时使用提供商默认模型
    /// </summary>
    private static string ResolveModel(string providerName, ProviderOptions options, string? model)
    {
        model ??= options.DefaultModel;

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException($"Model is required for provider '{providerName}'. Please configure DefaultModel in options.");
        }

        return model;
    }

    /// <summary>
    /// 根据提供商配置创建 OpenAIClient 实例
    /// </summary>
    private OpenAIClient CreateOpenAIClient(string providerName, ProviderOptions options)
    {
        // OpenAI：支持可选 BaseUrl，未配置时使用默认 endpoint
        if (string.Equals(providerName, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return CreateOpenAIProviderClient(options);
        }

        // Azure OpenAI：必须配置 BaseUrl
        if (string.Equals(providerName, "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return CreateAzureOpenAIProviderClient(options);
        }

        // 其他提供商（DeepSeek、Ollama 等）：按 OpenAI 兼容端点处理，要求配置 BaseUrl + ApiKey
        return CreateOpenAICompatibleProviderClient(providerName, options);
    }

    /// <summary>
    /// 创建 OpenAI 提供商的 OpenAIClient；若配置了 BaseUrl 则使用该 endpoint。
    /// </summary>
    private OpenAIClient CreateOpenAIProviderClient(ProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "OpenAI ApiKey is required. Please configure ApiKey in 'AI:Providers:OpenAI:ApiKey' or set environment variable 'AI__OPENAI__APIKEY'");
        }

        try
        {
            var credential = new ApiKeyCredential(options.ApiKey);

            if (!string.IsNullOrWhiteSpace(options.BaseUrl) && Uri.TryCreate(options.BaseUrl.TrimEnd('/'), UriKind.Absolute, out var endpoint) &&
                (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps))
            {
                return new OpenAIClient(credential, new OpenAIClientOptions { Endpoint = endpoint });
            }

            return new OpenAIClient(credential);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create OpenAI client: Provider=OpenAI");
            throw new InvalidOperationException($"Failed to create OpenAI client: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 创建 OpenAI 兼容的 OpenAIClient（DeepSeek、Ollama 等），要求 BaseUrl + ApiKey。
    /// </summary>
    private OpenAIClient CreateOpenAICompatibleProviderClient(string providerName, ProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                $"Provider '{providerName}' ApiKey is required. Configure 'AI:Providers:{providerName}:ApiKey' or set environment variable 'AI__{providerName.ToUpperInvariant()}__APIKEY'");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException(
                $"Provider '{providerName}' BaseUrl is required for OpenAI-compatible endpoint. Configure 'AI:Providers:{providerName}:BaseUrl'");
        }

        try
        {
            if (!Uri.TryCreate(options.BaseUrl.TrimEnd('/'), UriKind.Absolute, out var endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException($"Provider '{providerName}' BaseUrl '{options.BaseUrl}' is not a valid HTTP(S) URI");
            }

            var credential = new ApiKeyCredential(options.ApiKey);
            return new OpenAIClient(credential, new OpenAIClientOptions { Endpoint = endpoint });
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to create OpenAI-compatible client: Provider={Provider}, BaseUrl={BaseUrl}",
                providerName, options.BaseUrl);
            throw new InvalidOperationException($"Failed to create client for provider '{providerName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 创建 Azure OpenAI 提供商的 OpenAIClient，必须配置 BaseUrl。
    /// </summary>
    private OpenAIClient CreateAzureOpenAIProviderClient(ProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "AzureOpenAI ApiKey is required. Please configure ApiKey in 'AI:Providers:AzureOpenAI:ApiKey' or set environment variable 'AI__AZUREOPENAI__APIKEY'");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException(
                "AzureOpenAI BaseUrl is required. Please configure BaseUrl in 'AI:Providers:AzureOpenAI:BaseUrl'");
        }

        try
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var endpoint))
            {
                throw new InvalidOperationException($"AzureOpenAI BaseUrl '{options.BaseUrl}' is not a valid URI");
            }

            var credential = new ApiKeyCredential(options.ApiKey);
            return new OpenAIClient(credential, new OpenAIClientOptions { Endpoint = endpoint });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create AzureOpenAI client: Provider=AzureOpenAI, BaseUrl={BaseUrl}", options.BaseUrl);
            throw new InvalidOperationException($"Failed to create AzureOpenAI client: {ex.Message}", ex);
        }
    }
}
