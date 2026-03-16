
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// ChatClient 工厂 — 委托到 IChatClientProvider 创建客户端
/// </summary>
public class ChatClientFactory : IChatClientFactory
{
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly Dictionary<string, IChatClientProvider> _providers;
    private ConcurrentDictionary<string, IChatClient> _chatClients = new();
    private ConcurrentDictionary<string, IEmbeddingGenerator<string, Embedding<float>>> _embeddingClients = new();
    private ConcurrentDictionary<string, ChatClient> _openAIChatClients = new();
    private ConcurrentDictionary<string, EmbeddingClient> _openAIEmbeddingClients = new();
    private readonly ILogger<ChatClientFactory> _logger;

    public ChatClientFactory(
        IOptionsMonitor<AIOptions> options,
        IEnumerable<IChatClientProvider> providers,
        ILogger<ChatClientFactory> logger)
    {
        _options = Check.NotNull(options);
        Check.NotNull(providers);
        _logger = Check.NotNull(logger);

        // 配置热更新时原子替换整个字典引用，避免 Clear() 的竞态窗口
        _options.OnChange(_ =>
        {
            Volatile.Write(ref _chatClients, new ConcurrentDictionary<string, IChatClient>());
            Volatile.Write(ref _embeddingClients, new ConcurrentDictionary<string, IEmbeddingGenerator<string, Embedding<float>>>());
            Volatile.Write(ref _openAIChatClients, new ConcurrentDictionary<string, ChatClient>());
            Volatile.Write(ref _openAIEmbeddingClients, new ConcurrentDictionary<string, EmbeddingClient>());
        });

        // 按 ProviderName 建立索引（不区分大小写），后注册的覆盖先注册的
        _providers = new Dictionary<string, IChatClientProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            _providers[provider.ProviderName] = provider;
        }
    }

    /// <summary>
    /// 获取 IChatClient（MEAI 抽象）。当提供商配置了 FallbackProviders 时，自动包装降级链。
    /// </summary>
    public IChatClient GetChatClient(string? providerName = null, string? model = null)
    {
        var (name, providerOptions) = ResolveProvider(providerName);
        model = ResolveModel(name, providerOptions, model);

        var hasFallbacks = providerOptions.FallbackProviders is { Count: > 0 };
        var cacheKey = hasFallbacks
            ? $"meai-fb:{name}:{providerOptions.BaseUrl ?? "default"}:{model}"
            : $"meai:{name}:{providerOptions.BaseUrl ?? "default"}:{model}";

        var chatClients = Volatile.Read(ref _chatClients);
        return chatClients.GetOrAdd(cacheKey, _ =>
        {
            var provider = ResolveProviderImpl(name);
            var primaryClient = provider.CreateChatClient(providerOptions, model);

            if (!hasFallbacks) return primaryClient;

            var fallbackClients = BuildFallbackClients(providerOptions.FallbackProviders!);
            if (fallbackClients.Count == 0) return primaryClient;

            _logger.LogInformation("Provider '{Provider}' configured with {Count} fallback(s): {Fallbacks}",
                name, fallbackClients.Count, string.Join(" → ", providerOptions.FallbackProviders!));

            return new FallbackChatClient(primaryClient, fallbackClients, _logger);
        });
    }

    /// <summary>
    /// 获取 IEmbeddingGenerator（MEAI 抽象）
    /// </summary>
    public IEmbeddingGenerator<string, Embedding<float>>? GetEmbeddingGenerator(string? providerName = null, string? model = null)
    {
        var (name, providerOptions) = ResolveProvider(providerName);
        model = ResolveModel(name, providerOptions, model);

        var cacheKey = $"emb-meai:{name}:{providerOptions.BaseUrl ?? "default"}:{model}";

        var embeddingClients = Volatile.Read(ref _embeddingClients);
        return embeddingClients.GetOrAdd(cacheKey, _ =>
        {
            var provider = ResolveProviderImpl(name);
            var generator = provider.CreateEmbeddingGenerator(providerOptions, model);
            return generator ?? throw new InvalidOperationException(
                $"Provider '{name}' does not support embedding generation");
        });
    }

    /// <summary>
    /// 获取 OpenAI ChatClient（内部方法，通过 ChatClientFactoryExtensions 扩展方法访问）
    /// </summary>
    internal ChatClient GetOpenAIChatClientInternal(string? providerName = null, string? model = null)
    {
        var (name, providerOptions) = ResolveProvider(providerName);
        model = ResolveModel(name, providerOptions, model);

        var cacheKey = $"oai-chat:{name}:{providerOptions.BaseUrl ?? "default"}:{model}";

        var openAIChatClients = Volatile.Read(ref _openAIChatClients);
        return openAIChatClients.GetOrAdd(cacheKey, _ =>
        {
            var provider = ResolveProviderImpl(name);
            var nativeClient = provider.CreateNativeClient(providerOptions);
            if (nativeClient is OpenAIClient openAIClient)
            {
                return openAIClient.GetChatClient(model);
            }
            throw new InvalidOperationException(
                $"Provider '{name}' does not support OpenAI SDK types. Use GetChatClient() for MEAI abstraction.");
        });
    }

    /// <summary>
    /// 获取 OpenAI EmbeddingClient（内部方法，通过 ChatClientFactoryExtensions 扩展方法访问）
    /// </summary>
    internal EmbeddingClient GetOpenAIEmbeddingClientInternal(string? providerName = null, string? model = null)
    {
        var (name, providerOptions) = ResolveProvider(providerName);
        model = ResolveModel(name, providerOptions, model);

        var cacheKey = $"oai-emb:{name}:{providerOptions.BaseUrl ?? "default"}:{model}";

        var openAIEmbeddingClients = Volatile.Read(ref _openAIEmbeddingClients);
        return openAIEmbeddingClients.GetOrAdd(cacheKey, _ =>
        {
            var provider = ResolveProviderImpl(name);
            var nativeClient = provider.CreateNativeClient(providerOptions);
            if (nativeClient is OpenAIClient openAIClient)
            {
                return openAIClient.GetEmbeddingClient(model);
            }
            throw new InvalidOperationException(
                $"Provider '{name}' does not support OpenAI SDK types. Use GetEmbeddingGenerator() for MEAI abstraction.");
        });
    }

    /// <summary>
    /// 解析提供商实现
    /// </summary>
    private IChatClientProvider ResolveProviderImpl(string providerName)
    {
        // 1. 精确匹配
        if (_providers.TryGetValue(providerName, out var provider))
        {
            return provider;
        }

        // 2. 回退到 OpenAI 兼容（默认所有提供商都走 OpenAI 兼容协议）
        if (_providers.TryGetValue("OpenAI", out var openAIProvider))
        {
            return openAIProvider;
        }

        throw new InvalidOperationException(
            $"No IChatClientProvider registered for provider '{providerName}'. " +
            "Register a provider via services.AddSingleton<IChatClientProvider, YourProvider>().");
    }

    /// <summary>
    /// 解析并验证提供商配置
    /// </summary>
    private (string name, ProviderOptions options) ResolveProvider(string? providerName)
    {
        providerName ??= _options.CurrentValue.DefaultProvider;

        if (!_options.CurrentValue.Providers.TryGetValue(providerName, out var providerOptions))
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
    /// 获取所有已配置且启用的提供商名称列表
    /// </summary>
    public IReadOnlyList<string> GetAvailableProviders()
    {
        return _options.CurrentValue.Providers
            .Where(p => p.Value.Enabled)
            .Select(p => p.Key)
            .ToList();
    }

    /// <summary>
    /// 获取指定提供商的默认模型名称
    /// </summary>
    public string? GetDefaultModel(string? providerName = null)
    {
        providerName ??= _options.CurrentValue.DefaultProvider;
        return _options.CurrentValue.Providers.TryGetValue(providerName, out var opts)
            ? opts.DefaultModel
            : null;
    }

    /// <summary>
    /// 构建降级客户端列表
    /// </summary>
    /// <param name="fallbackSpecs">降级规格列表，格式: "ProviderName" 或 "ProviderName:ModelName"</param>
    private List<IChatClient> BuildFallbackClients(List<string> fallbackSpecs)
    {
        var clients = new List<IChatClient>();
        foreach (var spec in fallbackSpecs)
        {
            try
            {
                var parts = spec.Split(':', 2);
                var fbProviderName = parts[0];
                var fbModel = parts.Length > 1 ? parts[1] : null;

                var (name, fbOptions) = ResolveProvider(fbProviderName);
                var resolvedModel = ResolveModel(name, fbOptions, fbModel);
                var provider = ResolveProviderImpl(name);
                clients.Add(provider.CreateChatClient(fbOptions, resolvedModel));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create fallback client for spec '{Spec}', skipping", spec);
            }
        }
        return clients;
    }

    /// <summary>
    /// 解析模型名称（支持别名）
    /// </summary>
    private static string ResolveModel(string providerName, ProviderOptions options, string? model)
    {
        // 1. 别名解析：如果 model 是别名键，解析为实际模型名
        if (model != null && options.Models?.TryGetValue(model, out var aliased) == true)
            return aliased;

        // 2. 直接使用或回退默认
        model ??= options.DefaultModel;

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                $"Model is required for provider '{providerName}'. Please configure DefaultModel in options.");
        }

        return model;
    }
}
