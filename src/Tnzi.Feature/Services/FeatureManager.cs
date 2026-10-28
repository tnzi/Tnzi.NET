namespace Tnzi.Feature.Services;

/// <summary>
/// Default implementation of IFeatureManager.
/// Uses SemaphoreSlim + volatile snapshot for thread-safe caching.
/// Merges code-level definitions from IFeatureDefinitionProvider with database definitions.
/// Supports configurable cache expiration via CacheRefreshIntervalMinutes.
/// </summary>
public class FeatureManager : IFeatureManager, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEnumerable<IFeatureDefinitionProvider> _providers;
    private readonly IOptionsMonitor<FeatureOptions> _options;
    private readonly ILogger<FeatureManager> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private volatile IReadOnlyList<FeatureDefinitionRecord>? _snapshot;
    private volatile IReadOnlyDictionary<string, FeatureDefinitionRecord>? _snapshotDict;
    private long _snapshotTimeTicks;

    /// <summary>上一次成功从数据库读到的定义；读库失败时沿用它，而不是退成只剩代码定义。</summary>
    private volatile IReadOnlyList<FeatureDefinitionRecord>? _lastDatabaseDefinitions;

    /// <summary>当前快照是读库失败时拼出来的：到这个时刻（UTC ticks）就重试读库。0 = 快照完整。</summary>
    private long _retryAtTicks;

    /// <summary>
    /// 读库失败后多久重试。与 <see cref="FeatureOptions.CacheRefreshIntervalMinutes"/> 无关：
    /// 那个间隔是给一份完整快照的，降级快照不该再活 30 分钟（或在配成 0 时永远活着）。
    /// </summary>
    internal TimeSpan DegradedRetryInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initialize FeatureManager
    /// </summary>
    public FeatureManager(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IFeatureDefinitionProvider> providers,
        IOptionsMonitor<FeatureOptions> options,
        ILogger<FeatureManager> logger)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _providers = Check.NotNull(providers);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeatureDefinitionRecord>> GetAllAsync()
    {
        if (TryGetCachedSnapshot(out var snapshot))
            return snapshot!;

        return await RefreshSnapshotAsync();
    }

    /// <inheritdoc />
    public async Task<FeatureDefinitionRecord?> GetOrNullAsync(string name)
    {
        Check.NotNullOrWhiteSpace(name);

        if (!TryGetCachedSnapshot(out _))
        {
            await RefreshSnapshotAsync();
        }

        var dict = _snapshotDict;
        return dict != null && dict.TryGetValue(name, out var record) ? record : null;
    }

    /// <inheritdoc />
    public Task InvalidateCacheAsync()
    {
        _snapshot = null;
        _snapshotDict = null;
        Interlocked.Exchange(ref _snapshotTimeTicks, 0);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _semaphore.Dispose();
    }

    /// <summary>
    /// Check if the cached snapshot is still valid (not expired and not null)
    /// </summary>
    private bool TryGetCachedSnapshot(out IReadOnlyList<FeatureDefinitionRecord>? snapshot)
    {
        snapshot = _snapshot;
        if (snapshot == null)
            return false;

        var retryAt = Interlocked.Read(ref _retryAtTicks);
        if (retryAt != 0 && DateTime.UtcNow.Ticks >= retryAt)
        {
            // 降级快照到了重试时刻：丢掉它，下一次读取重新读库。
            _snapshot = null;
            _snapshotDict = null;
            snapshot = null;
            return false;
        }

        var cacheMinutes = _options.CurrentValue.CacheRefreshIntervalMinutes;
        var snapshotTime = new DateTime(Interlocked.Read(ref _snapshotTimeTicks), DateTimeKind.Utc);
        if (cacheMinutes > 0 && DateTime.UtcNow - snapshotTime >= TimeSpan.FromMinutes(cacheMinutes))
        {
            // Cache expired, invalidate
            _snapshot = null;
            _snapshotDict = null;
            snapshot = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Refresh the snapshot by merging code-level and database definitions
    /// </summary>
    private async Task<IReadOnlyList<FeatureDefinitionRecord>> RefreshSnapshotAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (TryGetCachedSnapshot(out var existing))
                return existing!;

            var merged = new Dictionary<string, FeatureDefinitionRecord>(StringComparer.OrdinalIgnoreCase);

            // 1. Collect code-level definitions
            var context = new FeatureDefinitionContext();
            foreach (var provider in _providers)
            {
                try
                {
                    provider.Define(context);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error defining features from provider {ProviderType}", provider.GetType().Name);
                }
            }

            foreach (var kvp in context.GetDefinitions())
            {
                merged[kvp.Key] = kvp.Value;
            }

            // 2. Merge database definitions (database overrides code-level)
            // ★ 读库失败不能当作一份完整快照缓存下去：那样一次数据库抖动恰好落在快照过期的那一刻，
            // 之后整个刷新间隔（默认 30 分钟，配成 0 时永远）里每一个数据库定义的功能都「不存在」，
            // IsEnabledAsync 一律 false，[RequireFeature] 端点对所有人 403，数据库早就恢复了也一样。
            // 所以失败时沿用上一次读到的数据库定义（没有就只剩代码定义），并在短间隔后重试。
            IReadOnlyList<FeatureDefinitionRecord>? databaseDefinitions;
            var degraded = false;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<FeatureDefinition, Guid>>();
                var dbDefinitions = await repository
                    .AsQueryable()
                    .AsNoTracking()
                    .ToListAsync();

                databaseDefinitions = dbDefinitions
                    .Select(dbDef => new FeatureDefinitionRecord(
                        Name: dbDef.Name,
                        DisplayName: dbDef.DisplayName,
                        Description: dbDef.Description,
                        DefaultValue: dbDef.DefaultValue,
                        ValueType: dbDef.ValueType,
                        ParentName: dbDef.ParentName,
                        IsEnabled: dbDef.IsEnabled,
                        Group: dbDef.Group))
                    .ToList()
                    .AsReadOnly();
                _lastDatabaseDefinitions = databaseDefinitions;
            }
            catch (Exception ex)
            {
                degraded = true;
                databaseDefinitions = _lastDatabaseDefinitions;
                _logger.LogWarning(ex,
                    databaseDefinitions == null
                        ? "Error loading feature definitions from database; using code-level definitions only and retrying in {RetrySeconds}s"
                        : "Error loading feature definitions from database; keeping the last loaded database definitions and retrying in {RetrySeconds}s",
                    DegradedRetryInterval.TotalSeconds);
            }

            foreach (var record in databaseDefinitions ?? [])
            {
                merged[record.Name] = record;
            }

            var list = merged.Values.ToList().AsReadOnly();
            var dict = merged.AsReadOnly();

            // 先写重试时刻与时间戳、再发布快照：读者先看快照再看这两个值，看到新快照时它们已经就位。
            var now = DateTime.UtcNow;
            Interlocked.Exchange(ref _retryAtTicks, degraded ? (now + DegradedRetryInterval).Ticks : 0);
            Interlocked.Exchange(ref _snapshotTimeTicks, now.Ticks);
            _snapshotDict = dict;
            _snapshot = list;

            return list;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
