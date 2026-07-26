namespace Tnzi.AI.Skills;

/// <summary>
/// 技能搜索服务实现 - Tier 1 关键词加权评分，Tier 2 语义降级（嵌入余弦相似度）。
/// </summary>
/// <remarks>
/// 评分权重：Name ×3.0 | Tags ×2.0 | WhenToUse ×1.5 | Description ×1.0
/// 语义降级：当关键词搜索结果不足且 IEmbeddingService 可用时，对未命中候选计算嵌入余弦相似度。
/// </remarks>
public class SkillSearchService : ISkillSearchService
{
    private readonly IEmbeddingService? _embeddingService;
    private readonly ILogger<SkillSearchService> _logger;
    private readonly IMemoryCache? _embeddingCache;
    private readonly IOptionsMonitor<AIOptions>? _options;

    private TimeSpan CacheTtl => _options?.CurrentValue.ContextProviders.Skills.CacheTtl ?? TimeSpan.FromMinutes(15);

    // 字段权重
    private const double NameWeight = 3.0;
    private const double TagWeight = 2.0;
    private const double WhenToUseWeight = 1.5;
    private const double DescriptionWeight = 1.0;

    // 语义搜索阈值
    private const double SemanticThreshold = 0.5;

    // 嵌入缓存 key 前缀
    private const string EmbeddingCachePrefix = "skill_embedding:";

    public SkillSearchService(
        ILogger<SkillSearchService> logger,
        IEmbeddingService? embeddingService = null,
        IMemoryCache? embeddingCache = null,
        IOptionsMonitor<AIOptions>? options = null)
    {
        _logger = Check.NotNull(logger);
        _embeddingService = embeddingService;
        _embeddingCache = embeddingCache;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SkillDefinition>> SearchAsync(
        IReadOnlyList<SkillDefinition> candidates,
        string query,
        int maxResults,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || candidates.Count == 0)
        {
            return [];
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Tier 1: 关键词搜索
        var scored = new List<(SkillDefinition Skill, double Score)>();
        foreach (var skill in candidates)
        {
            var score = ComputeScore(skill, tokens);
            if (score > 0)
            {
                scored.Add((skill, score));
            }
        }

        var keywordResults = scored
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => x.Skill)
            .ToList();

        // 关键词结果已满足 maxResults，无需语义降级
        if (keywordResults.Count >= maxResults)
        {
            return keywordResults;
        }

        // Tier 2: 语义降级 - 仅在关键词结果不足且嵌入服务可用时触发
        if (_embeddingService == null)
        {
            return keywordResults;
        }

        try
        {
            var remaining = maxResults - keywordResults.Count;
            var matchedSlugs = new HashSet<string>(keywordResults.Select(s => s.Slug));

            // 筛选未被关键词命中的候选
            var unmatchedCandidates = candidates.Where(c => !matchedSlugs.Contains(c.Slug)).ToList();
            if (unmatchedCandidates.Count == 0)
            {
                return keywordResults;
            }

            _logger.LogDebug(
                "Keyword search returned {KeywordCount}/{MaxResults} results, starting semantic fallback for {UnmatchedCount} unmatched candidates",
                keywordResults.Count, maxResults, unmatchedCandidates.Count);

            // 生成查询嵌入
            var queryEmbeddingResult = await _embeddingService.GenerateEmbeddingAsync(query, ct: ct);
            if (!queryEmbeddingResult.Succeeded || queryEmbeddingResult.Data == null)
            {
                _logger.LogDebug("Query embedding generation failed, returning keyword-only results");
                return keywordResults;
            }

            var queryVector = queryEmbeddingResult.Data!;

            // Get or generate embeddings for unmatched candidates (with caching)
            var candidateEmbeddings = await GetCandidateEmbeddingsAsync(unmatchedCandidates, ct);
            if (candidateEmbeddings == null)
            {
                _logger.LogDebug("Candidate embedding generation failed, returning keyword-only results");
                return keywordResults;
            }

            // 计算相似度并过滤
            var semanticScored = new List<(SkillDefinition Skill, double Score)>();
            for (var i = 0; i < unmatchedCandidates.Count && i < candidateEmbeddings.Count; i++)
            {
                var similarity = CosineSimilarity(queryVector, candidateEmbeddings[i]);
                if (similarity >= SemanticThreshold)
                {
                    semanticScored.Add((unmatchedCandidates[i], similarity));
                }
            }

            // 按相似度降序取所需数量
            var semanticResults = semanticScored
                .OrderByDescending(x => x.Score)
                .Take(remaining)
                .Select(x => x.Skill)
                .ToList();

            if (semanticResults.Count > 0)
            {
                _logger.LogDebug("Semantic fallback added {SemanticCount} results", semanticResults.Count);
                keywordResults.AddRange(semanticResults);
            }

            return keywordResults;
        }
        // 取消必须冒泡，不能被降级为"语义搜索失败"（与 DatabaseSkillStore 同一约定）
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 语义搜索失败不应影响关键词结果
            _logger.LogDebug(ex, "Semantic fallback search failed, returning keyword-only results");
            return keywordResults;
        }
    }

    private static double ComputeScore(SkillDefinition skill, string[] tokens)
    {
        double total = 0;
        foreach (var token in tokens)
        {
            // Name match
            if (!string.IsNullOrEmpty(skill.Name) &&
                skill.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                total += NameWeight;
            }

            // Tags match - any tag contains the token
            if (skill.Tags.Any(tag => tag.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                total += TagWeight;
            }

            // WhenToUse match
            if (!string.IsNullOrEmpty(skill.WhenToUse) &&
                skill.WhenToUse.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                total += WhenToUseWeight;
            }

            // Description match
            if (!string.IsNullOrEmpty(skill.Description) &&
                skill.Description.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                total += DescriptionWeight;
            }
        }
        return total;
    }

    /// <summary>
    /// Gets embeddings for candidates, using cache when available.
    /// </summary>
    private async Task<List<float[]>?> GetCandidateEmbeddingsAsync(List<SkillDefinition> candidates, CancellationToken ct)
    {
        var result = new List<float[]>(candidates.Count);
        var uncachedIndices = new List<int>();
        var uncachedTexts = new List<string>();

        for (var i = 0; i < candidates.Count; i++)
        {
            var cacheKey = EmbeddingCachePrefix + candidates[i].Slug;
            if (_embeddingCache != null && _embeddingCache.TryGetValue(cacheKey, out float[]? cached) && cached != null)
            {
                result.Add(cached);
            }
            else
            {
                result.Add([]); // placeholder
                uncachedIndices.Add(i);
                uncachedTexts.Add($"{candidates[i].Name} {candidates[i].Description} {candidates[i].WhenToUse}");
            }
        }

        // All cached
        if (uncachedIndices.Count == 0)
        {
            _logger.LogDebug("All {Count} candidate embeddings served from cache", candidates.Count);
            return result;
        }

        // Generate missing embeddings
        var embeddingsResult = await _embeddingService!.GenerateEmbeddingsAsync(uncachedTexts, ct: ct);
        if (!embeddingsResult.Succeeded || embeddingsResult.Data == null)
            return null;

        var generated = embeddingsResult.Data;
        for (var j = 0; j < uncachedIndices.Count && j < generated.Count; j++)
        {
            var idx = uncachedIndices[j];
            result[idx] = generated[j];

            // Cache the embedding
            if (_embeddingCache != null)
            {
                var cacheKey = EmbeddingCachePrefix + candidates[idx].Slug;
                _embeddingCache.Set(cacheKey, generated[j], CacheTtl);
            }
        }

        if (uncachedIndices.Count < candidates.Count)
            _logger.LogDebug("Embedding cache: {CachedCount} hits, {MissCount} generated", candidates.Count - uncachedIndices.Count, uncachedIndices.Count);

        return result;
    }

    /// <summary>
    /// 计算余弦相似度
    /// </summary>
    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0;
        }

        return System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(a, b);
    }
}
