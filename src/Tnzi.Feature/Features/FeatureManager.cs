namespace Tnzi.Feature.Features;

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
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<FeatureDefinition, Guid>>();
                var dbDefinitions = await repository
                    .AsQueryable()
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var dbDef in dbDefinitions)
                {
                    merged[dbDef.Name] = new FeatureDefinitionRecord(
                        Name: dbDef.Name,
                        DisplayName: dbDef.DisplayName,
                        Description: dbDef.Description,
                        DefaultValue: dbDef.DefaultValue,
                        ValueType: dbDef.ValueType,
                        ParentName: dbDef.ParentName,
                        IsEnabled: dbDef.IsEnabled,
                        Group: dbDef.Group);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading feature definitions from database, using code-level definitions only");
            }

            var list = merged.Values.ToList().AsReadOnly();
            var dict = merged.AsReadOnly();

            _snapshot = list;
            _snapshotDict = dict;
            Interlocked.Exchange(ref _snapshotTimeTicks, DateTime.UtcNow.Ticks);

            return list;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
