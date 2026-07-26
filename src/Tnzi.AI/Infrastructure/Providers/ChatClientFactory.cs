
using Microsoft.AspNetCore.DataProtection;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// ChatClient 工厂 - 委托到 IChatClientProvider 创建客户端
/// </summary>
public class ChatClientFactory : IChatClientFactory
{
    // TTL for DB-sourced provider options. Balances DB round-trip cost against
    // staleness after admin edits to the Provider entity.
    private static readonly TimeSpan DbProviderCacheTtl = TimeSpan.FromSeconds(60);

    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Dictionary<string, IChatClientProvider> _providers;
    private ConcurrentDictionary<string, IChatClient> _chatClients = new();
    private ConcurrentDictionary<string, IEmbeddingGenerator<string, Embedding<float>>> _embeddingClients = new();

    // DB-sourced provider cache. Lazy<T> wrapping ensures the DB query runs exactly
    // once per key per TTL window even under concurrent first-callers (stampede
    // protection). Null entry.Value.Options == confirmed-not-in-DB (negative cache).
    private readonly ConcurrentDictionary<string, Lazy<DbProviderCacheEntry>> _dbProviderCache
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<ChatClientFactory> _logger;

    private readonly record struct DbProviderCacheEntry(DateTime ExpiresAt, ProviderOptions? Options);

    public ChatClientFactory(
        IOptionsMonitor<AIOptions> options,
        IEnumerable<IChatClientProvider> providers,
        IServiceScopeFactory scopeFactory,
        ILogger<ChatClientFactory> logger)
    {
        _options = Check.NotNull(options);
        Check.NotNull(providers);
        _scopeFactory = Check.NotNull(scopeFactory);
        _logger = Check.NotNull(logger);

        // 配置热更新时原子替换整个字典引用，避免 Clear() 的竞态窗口
        // 同时 Dispose 旧的 IChatClient 实例，防止资源泄漏（HTTP 连接等）
        _options.OnChange(opts =>
        {
            var oldChatClients = Interlocked.Exchange(ref _chatClients, new ConcurrentDictionary<string, IChatClient>());
            var oldEmbeddingClients = Interlocked.Exchange(ref _embeddingClients, new ConcurrentDictionary<string, IEmbeddingGenerator<string, Embedding<float>>>());

            // Also clear DB provider cache so next resolution re-reads the DB in case
            // the admin updated both config and DB rows in tandem.
            _dbProviderCache.Clear();

            // 异步 Dispose 旧客户端，避免阻塞 OnChange 回调
            Task.Run(() => DisposeOldClientsAsync(oldChatClients, oldEmbeddingClients));
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
    /// 解析并验证提供商配置 - DB 记录优先于 appsettings 配置（override/补充层语义）。
    /// </summary>
    /// <remarks>
    /// Resolution order:
    ///   1. If a Provider entity exists in DB with matching Name AND IsEnabled, use it
    ///      (merged with matching config entry - DB fields override config fields).
    ///   2. Otherwise fall back to appsettings.json `AI:Providers` configuration.
    /// This gives admins a way to add/override providers at runtime without restart.
    /// DB lookups are cached for <see cref="DbProviderCacheTtl"/> to avoid hot-path
    /// round-trips; cache is invalidated on config hot-reload and on the TTL boundary.
    /// </remarks>
    private (string name, ProviderOptions options) ResolveProvider(string? providerName)
    {
        providerName ??= _options.CurrentValue.DefaultProvider;
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new InvalidOperationException(
                "No provider specified and no DefaultProvider configured in AI:DefaultProvider.");
        }

        var dbOptions = TryResolveFromDatabase(providerName);
        if (dbOptions is not null)
        {
            if (!dbOptions.Enabled)
            {
                throw new InvalidOperationException($"Provider '{providerName}' is disabled");
            }
            return (providerName, dbOptions);
        }

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
    /// Best-effort DB lookup for a Provider entity. Returns null if not found, the
    /// DB is unavailable, or the IRepository dependency isn't registered (EF Core
    /// module not loaded). Negative results are cached to avoid re-hitting the DB
    /// on every call. Lazy&lt;T&gt; wrapping ensures concurrent first-callers for the
    /// same key share a single DB query (stampede protection).
    /// </summary>
    private ProviderOptions? TryResolveFromDatabase(string providerName)
    {
        while (true)
        {
            var lazy = _dbProviderCache.GetOrAdd(providerName, key => new Lazy<DbProviderCacheEntry>(
                () => LoadProviderFromDatabase(key),
                LazyThreadSafetyMode.ExecutionAndPublication));

            var entry = lazy.Value;
            if (entry.ExpiresAt > DateTime.UtcNow)
            {
                return entry.Options;
            }

            // Expired - evict and retry so the next caller re-runs the factory.
            _dbProviderCache.TryRemove(new KeyValuePair<string, Lazy<DbProviderCacheEntry>>(providerName, lazy));
        }
    }

    private DbProviderCacheEntry LoadProviderFromDatabase(string providerName)
    {
        var expiresAt = DateTime.UtcNow.Add(DbProviderCacheTtl);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetService<IRepository<Entities.Provider, Guid>>();
            if (repository is null)
            {
                return new DbProviderCacheEntry(expiresAt, null);
            }

            var entity = repository.AsQueryable()
                .Where(p => p.Name == providerName && p.IsEnabled)
                .OrderByDescending(p => p.Priority)
                .FirstOrDefault();

            if (entity is null)
            {
                return new DbProviderCacheEntry(expiresAt, null);
            }

            string? apiKey = null;
            if (!string.IsNullOrEmpty(entity.ApiKeyEncrypted))
            {
                try
                {
                    var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                        .CreateProtector(Entities.Provider.ApiKeyProtectorPurpose);
                    apiKey = protector.Unprotect(entity.ApiKeyEncrypted);
                }
                catch (Exception ex)
                {
                    // Data protection key ring rotation is the common cause - re-saving
                    // the provider re-encrypts with the current key. Fall back to config.
                    _logger.LogError(ex,
                        "Failed to decrypt ApiKey for Provider '{Name}' (id={Id}); falling back to configuration",
                        entity.Name, entity.Id);
                    return new DbProviderCacheEntry(expiresAt, null);
                }
            }

            // Start from the matching config entry (if any) so config-only fields
            // (TimeoutSeconds, FallbackProviders, Models, Thinking, etc.) survive.
            var configBase = _options.CurrentValue.Providers.TryGetValue(providerName, out var configOpts)
                ? configOpts
                : new ProviderOptions();

            var resolved = new ProviderOptions
            {
                Name = entity.Name,
                Enabled = entity.IsEnabled,
                ApiKey = apiKey ?? configBase.ApiKey,
                BaseUrl = entity.Endpoint ?? configBase.BaseUrl,
                DefaultModel = entity.DefaultModel ?? configBase.DefaultModel,
                TimeoutSeconds = configBase.TimeoutSeconds,
                MaxTokens = configBase.MaxTokens,
                Temperature = configBase.Temperature,
                FallbackProviders = configBase.FallbackProviders,
                Models = configBase.Models,
                Thinking = configBase.Thinking,
                PromptCaching = configBase.PromptCaching,
                ContextWindowSize = configBase.ContextWindowSize
            };

            return new DbProviderCacheEntry(expiresAt, resolved);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to resolve Provider '{Name}' from database, falling back to configuration", providerName);
            return new DbProviderCacheEntry(expiresAt, null);
        }
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
                // Elevated to Error because fallback misconfiguration silently
                // weakens resilience: the primary provider stays up but the
                // degraded-mode path is unavailable until an operator notices.
                _logger.LogError(ex, "Failed to create fallback client for spec '{Spec}', skipping", spec);
            }
        }
        return clients;
    }

    /// <summary>
    /// 异步释放旧的客户端实例，防止配置热更新时资源泄漏
    /// </summary>
    private async Task DisposeOldClientsAsync(
        ConcurrentDictionary<string, IChatClient> oldChatClients,
        ConcurrentDictionary<string, IEmbeddingGenerator<string, Embedding<float>>> oldEmbeddingClients)
    {
        foreach (var client in oldChatClients.Values)
        {
            try
            {
                if (client is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (client is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose old IChatClient: {Type}", client.GetType().Name);
            }
        }

        foreach (var client in oldEmbeddingClients.Values)
        {
            try
            {
                if (client is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (client is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose old IEmbeddingGenerator: {Type}", client.GetType().Name);
            }
        }
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
