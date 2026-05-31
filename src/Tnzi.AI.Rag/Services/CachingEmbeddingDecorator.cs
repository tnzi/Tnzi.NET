namespace Tnzi.AI.Rag.Services;

/// <summary>
/// Embedding cache decorator — caches embedding results to avoid redundant API calls.
/// <para>
/// Uses content-based SHA256 hash as cache key. When the same text (with same provider+model)
/// is embedded again, the cached result is returned without calling the embedding API.
/// </para>
/// <para>
/// Cache is stored using the framework's ICache abstraction (Redis, Memory, etc.).
/// Cache entries expire after a configurable TTL (default: 7 days).
/// </para>
/// </summary>
public class CachingEmbeddingDecorator : IEmbeddingService
{
    /// <summary>
    /// Keyed service key for the inner (non-cached) embedding service
    /// </summary>
    public const string InnerServiceKey = "tnzi:embedding:inner";

    private readonly IEmbeddingService _inner;
    private readonly ICache? _cache;
    private readonly AIRagOptions _options;
    private readonly ILogger<CachingEmbeddingDecorator> _logger;
    private readonly ICurrentTenant? _currentTenant;

    /// <summary>
    /// Cache key prefix for embedding cache entries
    /// </summary>
    private const string CacheKeyPrefix = "tnzi:embedding:";

    public CachingEmbeddingDecorator(
        [FromKeyedServices(InnerServiceKey)] IEmbeddingService inner,
        ILogger<CachingEmbeddingDecorator> logger,
        IOptions<AIRagOptions> options,
        ICache? cache = null,
        ICurrentTenant? currentTenant = null)
    {
        _inner = Check.NotNull(inner);
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options).Value;
        _cache = cache;
        _currentTenant = currentTenant;
    }

    public async Task<Result<float[]>> GenerateEmbeddingAsync(string text, EmbeddingOptions? options = null, CancellationToken ct = default)
    {
        if (_cache == null || !_options.EmbeddingCache.Enabled)
        {
            return await _inner.GenerateEmbeddingAsync(text, options, ct);
        }

        var cacheKey = BuildCacheKey(text, options);

        // Try to get from cache
        var cached = await _cache.GetAsync<float[]>(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogDebug("Embedding cache hit: {CacheKey}", cacheKey);
            RagActivitySource.RecordEmbeddingCacheHit();
            return Result<float[]>.Success(cached);
        }

        // Cache miss — call the real embedding service
        var result = await _inner.GenerateEmbeddingAsync(text, options, ct);

        if (result.Succeeded && result.Data != null)
        {
            // Store in cache with TTL
            var ttl = TimeSpan.FromHours(_options.EmbeddingCache.TtlHours);
            await _cache.SetAsync(cacheKey, result.Data, ttl, ct);
            _logger.LogDebug("Embedding cached: {CacheKey}, TTL={TtlHours}h", cacheKey, _options.EmbeddingCache.TtlHours);
            RagActivitySource.RecordEmbeddingCacheMiss();
        }

        return result;
    }

    public async Task<Result<List<float[]>>> GenerateEmbeddingsAsync(List<string> texts, EmbeddingOptions? options = null, CancellationToken ct = default)
    {
        if (_cache == null || !_options.EmbeddingCache.Enabled)
        {
            return await _inner.GenerateEmbeddingsAsync(texts, options, ct);
        }

        var results = new float[texts.Count][];
        var uncachedIndices = new List<int>();
        var uncachedTexts = new List<string>();

        // Check cache for each text
        for (var i = 0; i < texts.Count; i++)
        {
            var cacheKey = BuildCacheKey(texts[i], options);
            var cached = await _cache.GetAsync<float[]>(cacheKey, ct);

            if (cached != null)
            {
                results[i] = cached;
                RagActivitySource.RecordEmbeddingCacheHit();
            }
            else
            {
                uncachedIndices.Add(i);
                uncachedTexts.Add(texts[i]);
                RagActivitySource.RecordEmbeddingCacheMiss();
            }
        }

        _logger.LogDebug("Embedding batch: {Total} total, {Cached} cached, {Uncached} to generate",
            texts.Count, texts.Count - uncachedTexts.Count, uncachedTexts.Count);

        // Generate embeddings for uncached texts
        if (uncachedTexts.Count > 0)
        {
            var batchResult = await _inner.GenerateEmbeddingsAsync(uncachedTexts, options, ct);
            if (!batchResult.Succeeded)
            {
                return batchResult;
            }

            var ttl = TimeSpan.FromHours(_options.EmbeddingCache.TtlHours);

            // Fill in results and cache
            for (var i = 0; i < uncachedIndices.Count; i++)
            {
                var originalIndex = uncachedIndices[i];
                results[originalIndex] = batchResult.Data![i];

                var cacheKey = BuildCacheKey(texts[originalIndex], options);
                await _cache.SetAsync(cacheKey, batchResult.Data![i], ttl, ct);
            }
        }

        return Result<List<float[]>>.Success(results.ToList());
    }

    /// <summary>
    /// Build a cache key from text content and options using SHA256 hash
    /// </summary>
    private string BuildCacheKey(string text, EmbeddingOptions? options)
    {
        var provider = options?.Provider ?? "default";
        var model = options?.Model ?? "default";
        var tenantId = _currentTenant?.Id?.ToString("N") ?? "global";
        var input = $"{tenantId}:{provider}:{model}:{text}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"{CacheKeyPrefix}{Convert.ToHexStringLower(hash)}";
    }
}
