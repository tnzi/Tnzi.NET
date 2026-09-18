

namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// ChatClient 工厂 - 委托到 IChatClientProvider 创建客户端
/// </summary>
/// <remarks>
/// <para>
/// ★ 数据库 Provider 的解析带租户维度。<c>Provider</c> 的唯一索引是 (Name, Scope, TenantId)：
/// 两个租户可以各自登记同名 Provider，端点与密钥都不同；本工厂是 Singleton，若只按名字分桶，
/// 先解析的那个租户会把自己的端点与密钥留给所有租户（含 <c>IAiUtility</c> 这类宿主级调用）。
/// 租户从工厂自己开的作用域里读 <see cref="ICurrentTenant"/>（它是 AsyncLocal 承载的，随执行流传播），
/// 与 <c>McpServerCatalog</c> 同形；解析规则是「本租户行优先，其次 System 行」，
/// <b>没有租户上下文时只看 System 行</b> —— 宿主级调用不该拿着某个租户登记的凭据往外打。
/// </para>
/// <para>
/// 缓存分区：只有解析到<b>租户行</b>时键才带租户；System 行与部署配置是进程级同一份凭据，
/// 所有租户共用一个客户端，单租户部署与引入租户维度之前逐字相同。
/// </para>
/// </remarks>
public class ChatClientFactory : IChatClientFactory
{
    // TTL for DB-sourced provider options. Balances DB round-trip cost against
    // staleness after admin edits to the Provider entity.
    private static readonly TimeSpan DbProviderCacheTtl = TimeSpan.FromSeconds(60);

    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Dictionary<string, IChatClientProvider> _providers;
    private ConcurrentDictionary<ClientCacheKey, IChatClient> _chatClients = new();
    private ConcurrentDictionary<ClientCacheKey, IEmbeddingGenerator<string, Embedding<float>>> _embeddingClients = new();

    // DB-sourced provider cache, partitioned by (tenant, name). Lazy<T> wrapping ensures
    // the DB query runs exactly once per key per TTL window even under concurrent
    // first-callers (stampede protection). Null entry.Value.Options == confirmed-not-in-DB
    // (negative cache).
    private readonly ConcurrentDictionary<DbProviderCacheKey, Lazy<DbProviderCacheEntry>> _dbProviderCache = new();

    private readonly ILogger<ChatClientFactory> _logger;

    /// <summary>
    /// 提供商消息处理器（Kimi / GLM / MiniMax 的 &lt;think&gt; 标签等）。此前只被注册、从未被调用；
    /// 现在按 <see cref="ChatMessageProcessorSelector"/> 的判定以 <see cref="MessageProcessingChatClient"/> 包在 SDK 客户端外面。
    /// </summary>
    private readonly List<IChatMessageProcessor> _messageProcessors;


    /// <summary>
    /// 数据库 Provider 缓存项。<paramref name="TenantPartition"/> 非空表示解析到的是某个租户自己的行，
    /// 客户端缓存必须按它分区；System 行与配置条目为 null（进程级共用）。
    /// </summary>
    private readonly record struct DbProviderCacheEntry(DateTime ExpiresAt, ProviderOptions? Options, string? TenantPartition);

    /// <summary>数据库解析缓存键：租户（无则空串）+ 名字（忽略大小写）。</summary>
    private readonly record struct DbProviderCacheKey(string TenantKey, string Name)
    {
        public bool Equals(DbProviderCacheKey other) =>
            TenantKey == other.TenantKey && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() =>
            HashCode.Combine(TenantKey, StringComparer.OrdinalIgnoreCase.GetHashCode(Name));
    }

    /// <summary>
    /// 客户端缓存键。<paramref name="TenantPartition"/> 只在解析到租户行时非空；
    /// <paramref name="Credential"/> 是主提供商与整条降级链的密钥指纹 —— 客户端在创建时固化密钥，
    /// 不把它并进键，密钥轮换后 DB 缓存到 TTL 重读到新密钥、客户端缓存却照样命中旧客户端，
    /// 旧钥一吊销就 401 到进程重启（单实例有 InvalidateProvider 兜底，多实例只有这条）。
    /// </summary>
    private readonly record struct ClientCacheKey(string? TenantPartition, string Kind, string ProviderName, string BaseUrl, string Model, string Credential);

    /// <summary>密钥指纹：SHA-256 前 8 字节；只进缓存键，永不记日志。</summary>
    private static string CredentialFingerprint(ProviderOptions options)
        => string.IsNullOrEmpty(options.ApiKey)
            ? "-"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.ApiKey)), 0, 8);

    private static string ChainCredentialFingerprint(ProviderOptions primary, List<ResolvedFallback> fallbacks)
        => fallbacks.Count == 0
            ? CredentialFingerprint(primary)
            : string.Join("|", fallbacks.Select(f => CredentialFingerprint(f.Provider.Options)).Prepend(CredentialFingerprint(primary)));

    /// <summary>
    /// 只让数据库解析缓存过期（客户端缓存不动）—— 模拟另一实例到 TTL 重读的形态，供测试用。
    /// </summary>
    internal void ExpireDatabaseProviderCacheForTesting() => _dbProviderCache.Clear();

    /// <summary>解析结果：提供商名、生效的选项、以及客户端缓存的租户分区（见 <see cref="DbProviderCacheEntry"/>）。</summary>
    private readonly record struct ResolvedProvider(string Name, ProviderOptions Options, string? TenantPartition);

    public ChatClientFactory(
        IOptionsMonitor<AIOptions> options,
        IEnumerable<IChatClientProvider> providers,
        IServiceScopeFactory scopeFactory,
        ILogger<ChatClientFactory> logger,
        IEnumerable<IChatMessageProcessor>? messageProcessors = null)
    {
        _options = Check.NotNull(options);
        Check.NotNull(providers);
        _scopeFactory = Check.NotNull(scopeFactory);
        _logger = Check.NotNull(logger);
        _messageProcessors = messageProcessors?.ToList() ?? [];

        // 配置热更新时原子替换整个字典引用，避免 Clear() 的竞态窗口
        // 同时 Dispose 旧的 IChatClient 实例，防止资源泄漏（HTTP 连接等）
        _options.OnChange(opts =>
        {
            var oldChatClients = Interlocked.Exchange(ref _chatClients, new ConcurrentDictionary<ClientCacheKey, IChatClient>());
            var oldEmbeddingClients = Interlocked.Exchange(ref _embeddingClients, new ConcurrentDictionary<ClientCacheKey, IEmbeddingGenerator<string, Embedding<float>>>());

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
        // 租户只读一次：主提供商与整条降级链必须按同一个调用方解析
        var tenantId = ResolveCurrentTenantId();
        var (name, providerOptions, tenantPartition) = ResolveProvider(providerName, tenantId);
        model = ResolveModel(name, providerOptions, model);

        // 降级项按调用方租户解析（租户行 > System），所以链的分区不只是主提供商的分区：
        // 主提供商来自配置（分区 null）而某个降级名字命中了租户行时，用那个租户密钥建的降级客户端
        // 不能挂在进程级的键下。先把整条链解析出来，把每一项的分区都并进缓存键。
        var hasFallbacks = providerOptions.FallbackProviders is { Count: > 0 };
        var fallbacks = hasFallbacks ? ResolveFallbackChain(providerOptions.FallbackProviders!, tenantId) : [];
        var cacheKey = new ClientCacheKey(
            BuildChainPartition(tenantPartition, fallbacks), hasFallbacks ? "meai-fb" : "meai", name, providerOptions.BaseUrl ?? "default", model,
            ChainCredentialFingerprint(providerOptions, fallbacks));

        var chatClients = Volatile.Read(ref _chatClients);
        return chatClients.GetOrAdd(cacheKey, _ =>
        {
            var provider = ResolveProviderImpl(name, providerOptions);
            var primaryClient = WrapWithMessageProcessor(provider.CreateChatClient(providerOptions, model), providerOptions, model);

            if (!hasFallbacks) return primaryClient;

            var fallbackClients = BuildFallbackClients(fallbacks);
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
        var (name, providerOptions, tenantPartition) = ResolveProvider(providerName);
        model = ResolveModel(name, providerOptions, model);

        var cacheKey = new ClientCacheKey(tenantPartition, "emb-meai", name, providerOptions.BaseUrl ?? "default", model, CredentialFingerprint(providerOptions));

        var embeddingClients = Volatile.Read(ref _embeddingClients);
        return embeddingClients.GetOrAdd(cacheKey, _ =>
        {
            var provider = ResolveProviderImpl(name, providerOptions);
            var generator = provider.CreateEmbeddingGenerator(providerOptions, model);
            return generator ?? throw new InvalidOperationException(
                $"Provider '{name}' does not support embedding generation");
        });
    }

    /// <summary>
    /// 提供商需要消息处理器（&lt;think&gt; 标签剥离等）时包一层，否则原样返回。
    /// </summary>
    private IChatClient WrapWithMessageProcessor(IChatClient client, ProviderOptions providerOptions, string model)
    {
        if (_messageProcessors.Count == 0)
        {
            return client;
        }

        var processor = ChatMessageProcessorSelector.Select(_messageProcessors, providerOptions, model);
        if (processor is null)
        {
            return client;
        }

        _logger.LogDebug("Provider '{Provider}' model '{Model}' uses message processor '{Processor}'",
            providerOptions.Name, model, processor.ProviderName);
        return new MessageProcessingChatClient(client, processor);
    }

    /// <summary>
    /// 解析提供商实现：ProviderType 命中 → 名字命中 → OpenAI 兼容
    /// </summary>
    private IChatClientProvider ResolveProviderImpl(string providerName, ProviderOptions options)
    {
        // 1. 显式声明的协议（数据库行的 Provider.ProviderType / 配置条目的 ProviderType）
        if (!string.IsNullOrWhiteSpace(options.ProviderType)
            && _providers.TryGetValue(options.ProviderType, out var typedProvider))
        {
            return typedProvider;
        }

        // 2. 名字精确匹配（"Anthropic" 这个名字本身就是原生 Anthropic 协议）
        if (_providers.TryGetValue(providerName, out var provider))
        {
            return provider;
        }

        // 3. 回退到 OpenAI 兼容（默认所有提供商都走 OpenAI 兼容协议）
        if (_providers.TryGetValue("OpenAI", out var openAIProvider))
        {
            return openAIProvider;
        }

        throw new InvalidOperationException(
            $"No IChatClientProvider registered for provider '{providerName}' (type '{options.ProviderType}'). " +
            "Register a provider via services.AddSingleton<IChatClientProvider, YourProvider>().");
    }

    /// <inheritdoc />
    public void InvalidateProvider(string providerName)
    {
        Check.NotNullOrWhiteSpace(providerName);

        // 调用方（Provider CRUD）只带得出名字，本工厂是 Singleton 没有租户上下文，
        // 因此清掉全部租户桶里叫这个名字的条目。多清一个只是多读一次库；
        // 漏清一个是继续拿着刚被改掉的端点与密钥往外打，且毫无症状。
        foreach (var key in _dbProviderCache.Keys.Where(k => NameMatches(k.Name, providerName)).ToList())
        {
            _dbProviderCache.TryRemove(key, out _);
        }

        var chatClients = Volatile.Read(ref _chatClients);
        var evictedChatClients = new ConcurrentDictionary<ClientCacheKey, IChatClient>();
        foreach (var key in chatClients.Keys.Where(k => NameMatches(k.ProviderName, providerName)).ToList())
        {
            if (chatClients.TryRemove(key, out var client))
            {
                evictedChatClients[key] = client;
            }
        }

        var embeddingClients = Volatile.Read(ref _embeddingClients);
        var evictedEmbeddingClients = new ConcurrentDictionary<ClientCacheKey, IEmbeddingGenerator<string, Embedding<float>>>();
        foreach (var key in embeddingClients.Keys.Where(k => NameMatches(k.ProviderName, providerName)).ToList())
        {
            if (embeddingClients.TryRemove(key, out var generator))
            {
                evictedEmbeddingClients[key] = generator;
            }
        }

        if (!evictedChatClients.IsEmpty || !evictedEmbeddingClients.IsEmpty)
        {
            Task.Run(() => DisposeOldClientsAsync(evictedChatClients, evictedEmbeddingClients));
        }

        _logger.LogDebug("Invalidated cached provider '{Provider}' across all tenant partitions", providerName);
    }

    private static bool NameMatches(string cachedName, string providerName) =>
        string.Equals(cachedName, providerName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解析并验证提供商配置 - DB 记录优先于 appsettings 配置（override/补充层语义）。
    /// </summary>
    /// <remarks>
    /// Resolution order:
    ///   1. If a Provider entity visible to the caller's tenant exists in DB with matching
    ///      Name AND IsEnabled, use it (merged with matching config entry - DB fields override
    ///      config fields). The caller's own tenant row wins over a System row; with no tenant
    ///      context only System rows are considered.
    ///   2. Otherwise fall back to appsettings.json `AI:Providers` configuration.
    /// This gives admins a way to add/override providers at runtime without restart.
    /// DB lookups are cached for <see cref="DbProviderCacheTtl"/> per (tenant, name) to avoid
    /// hot-path round-trips; cache is invalidated on config hot-reload, on Provider CRUD
    /// (<see cref="InvalidateProvider"/>) and on the TTL boundary.
    /// </remarks>
    private ResolvedProvider ResolveProvider(string? providerName) => ResolveProvider(providerName, ResolveCurrentTenantId());

    /// <summary>同上，但由调用方给定租户（一次调用里解析多个提供商时只开一次作用域）。</summary>
    private ResolvedProvider ResolveProvider(string? providerName, Guid? tenantId)
    {
        providerName ??= _options.CurrentValue.DefaultProvider;
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new InvalidOperationException(
                "No provider specified and no DefaultProvider configured in AI:DefaultProvider.");
        }

        var dbEntry = TryResolveFromDatabase(providerName, tenantId);
        if (dbEntry.Options is not null)
        {
            if (!dbEntry.Options.Enabled)
            {
                throw new InvalidOperationException($"Provider '{providerName}' is disabled");
            }
            return new ResolvedProvider(providerName, dbEntry.Options, dbEntry.TenantPartition);
        }

        if (!_options.CurrentValue.Providers.TryGetValue(providerName, out var providerOptions))
        {
            throw new InvalidOperationException($"Provider '{providerName}' not found in configuration");
        }

        if (!providerOptions.Enabled)
        {
            throw new InvalidOperationException($"Provider '{providerName}' is disabled");
        }

        return new ResolvedProvider(providerName, providerOptions, null);
    }

    /// <summary>
    /// 取调用方的租户：<see cref="ICurrentTenant"/> 是 Scoped 且 AsyncLocal 承载，
    /// Singleton 只能经新作用域读到它（与 <c>McpServerCatalog</c> 同形）。未注册或无租户时为 null。
    /// </summary>
    private Guid? ResolveCurrentTenantId()
    {
        using var scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<ICurrentTenant>()?.Id;
    }

    /// <summary>
    /// Best-effort DB lookup for a Provider entity. Returns null if not found, the
    /// DB is unavailable, or the IRepository dependency isn't registered (EF Core
    /// module not loaded). Negative results are cached to avoid re-hitting the DB
    /// on every call. Lazy&lt;T&gt; wrapping ensures concurrent first-callers for the
    /// same key share a single DB query (stampede protection).
    /// </summary>
    private DbProviderCacheEntry TryResolveFromDatabase(string providerName, Guid? tenantId)
    {
        var cacheKey = new DbProviderCacheKey(tenantId?.ToString() ?? string.Empty, providerName);
        while (true)
        {
            var lazy = _dbProviderCache.GetOrAdd(cacheKey, key => new Lazy<DbProviderCacheEntry>(
                () => LoadProviderFromDatabase(key.Name, tenantId),
                LazyThreadSafetyMode.ExecutionAndPublication));

            var entry = lazy.Value;
            if (entry.ExpiresAt > DateTime.UtcNow)
            {
                return entry;
            }

            // Expired - evict and retry so the next caller re-runs the factory.
            _dbProviderCache.TryRemove(new KeyValuePair<DbProviderCacheKey, Lazy<DbProviderCacheEntry>>(cacheKey, lazy));
        }
    }

    private DbProviderCacheEntry LoadProviderFromDatabase(string providerName, Guid? tenantId)
    {
        var expiresAt = DateTime.UtcNow.Add(DbProviderCacheTtl);
        var miss = new DbProviderCacheEntry(expiresAt, null, null);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetService<IRepository<Entities.Provider, Guid>>();
            if (repository is null)
            {
                return miss;
            }

            // 本租户行优先于 System 行（同一层内按 Priority）；无租户上下文时只看 System 行。
            // Provider 刻意不是 IMultiTenant（System 行要跨租户可见），全局过滤器帮不上忙，谓词必须自己写。
            var candidates = repository.AsQueryable().Where(p => p.Name == providerName && p.IsEnabled);
            candidates = tenantId.HasValue
                ? candidates.Where(p => p.Scope == ResourceScope.System
                                        || (p.Scope == ResourceScope.Tenant && p.TenantId == tenantId))
                : candidates.Where(p => p.Scope == ResourceScope.System);

            var entity = candidates
                .OrderByDescending(p => p.Scope == ResourceScope.Tenant)
                .ThenByDescending(p => p.Priority)
                .FirstOrDefault();

            if (entity is null)
            {
                return miss;
            }

            var tenantPartition = entity.Scope == ResourceScope.Tenant ? entity.TenantId?.ToString() : null;

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
                    return miss;
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
                // 实体注释一直写着「用作 IChatClientFactory 选择 SDK 的 key」，这里此前根本不复制它
                ProviderType = string.IsNullOrWhiteSpace(entity.ProviderType) ? configBase.ProviderType : entity.ProviderType,
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

            return new DbProviderCacheEntry(expiresAt, resolved, tenantPartition);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to resolve Provider '{Name}' from database, falling back to configuration", providerName);
            return miss;
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

    /// <summary>已解析的降级项：提供商 + 生效模型 + 原始规格（日志用）。</summary>
    private readonly record struct ResolvedFallback(ResolvedProvider Provider, string Model, string Spec);

    /// <summary>
    /// 按调用方租户解析整条降级链。解析失败的项记 Error 后跳过（与建客户端失败同一口径）。
    /// </summary>
    /// <param name="fallbackSpecs">降级规格列表，格式: "ProviderName" 或 "ProviderName:ModelName"</param>
    /// <param name="tenantId">调用方租户（与主提供商同一次读取）</param>
    private List<ResolvedFallback> ResolveFallbackChain(List<string> fallbackSpecs, Guid? tenantId)
    {
        var resolved = new List<ResolvedFallback>(fallbackSpecs.Count);
        foreach (var spec in fallbackSpecs)
        {
            try
            {
                var parts = spec.Split(':', 2);
                var fbProviderName = parts[0];
                var fbModel = parts.Length > 1 ? parts[1] : null;

                var provider = ResolveProvider(fbProviderName, tenantId);
                resolved.Add(new ResolvedFallback(provider, ResolveModel(provider.Name, provider.Options, fbModel), spec));
            }
            catch (Exception ex)
            {
                // Elevated to Error because fallback misconfiguration silently
                // weakens resilience: the primary provider stays up but the
                // degraded-mode path is unavailable until an operator notices.
                _logger.LogError(ex, "Failed to resolve fallback provider for spec '{Spec}', skipping", spec);
            }
        }
        return resolved;
    }

    /// <summary>
    /// 链的缓存分区：主提供商与每个降级项的分区都是 null 时为 null（进程级共用，与无降级时逐字相同）；
    /// 任一项命中租户行，就把各项分区按位拼起来，让不同租户看到的链各自成键。
    /// </summary>
    private static string? BuildChainPartition(string? primaryPartition, List<ResolvedFallback> fallbacks)
    {
        if (primaryPartition is null && fallbacks.TrueForAll(f => f.Provider.TenantPartition is null))
        {
            return null;
        }
        return string.Join("|", fallbacks.Select(f => f.Provider.TenantPartition ?? string.Empty).Prepend(primaryPartition ?? string.Empty));
    }

    /// <summary>
    /// 为已解析的降级链建客户端
    /// </summary>
    private List<IChatClient> BuildFallbackClients(List<ResolvedFallback> fallbacks)
    {
        var clients = new List<IChatClient>(fallbacks.Count);
        foreach (var (provider, model, spec) in fallbacks)
        {
            try
            {
                clients.Add(WrapWithMessageProcessor(
                    ResolveProviderImpl(provider.Name, provider.Options).CreateChatClient(provider.Options, model), provider.Options, model));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create fallback client for spec '{Spec}', skipping", spec);
            }
        }
        return clients;
    }

    /// <summary>
    /// 异步释放旧的客户端实例，防止配置热更新时资源泄漏
    /// </summary>
    private async Task DisposeOldClientsAsync(
        ConcurrentDictionary<ClientCacheKey, IChatClient> oldChatClients,
        ConcurrentDictionary<ClientCacheKey, IEmbeddingGenerator<string, Embedding<float>>> oldEmbeddingClients)
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
