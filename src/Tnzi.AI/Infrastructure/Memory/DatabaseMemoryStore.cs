namespace Tnzi.AI.Infrastructure.Memory;

/// <summary>
/// 数据库记忆存储 — 使用 EF Core 持久化记忆
/// </summary>
/// <remarks>
/// 支持混合搜索模式：当 IEmbeddingService 可用时，使用 70% 向量相似度 + 30% 关键词匹配的融合评分；
/// 否则降级为纯关键词搜索（向后兼容）。
/// 融合评分后叠加重要性权重 (Importance) 和时间衰减 (RecencyDecayRate)。
/// </remarks>
public partial class DatabaseMemoryStore : IMemoryStore
{
    private readonly IRepository<MemoryEntry, Guid> _repository;
    private readonly IUnitOfWorkManager? _unitOfWorkManager;
    private readonly IEmbeddingService? _embeddingService;
    private readonly ILogger<DatabaseMemoryStore> _logger;
    private readonly MemoryOptions? _memoryOptions;

    private const double VectorWeight = 0.7;
    private const double KeywordWeight = 0.3;

    public DatabaseMemoryStore(
        IRepository<MemoryEntry, Guid> repository,
        ILogger<DatabaseMemoryStore> logger,
        IUnitOfWorkManager? unitOfWorkManager = null,
        IEmbeddingService? embeddingService = null,
        IOptions<AIOptions>? aiOptions = null)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
        _unitOfWorkManager = unitOfWorkManager;
        _embeddingService = embeddingService;
        _memoryOptions = aiOptions?.Value.ContextProviders.Memory;

        // 权重求和验证
        var scoring = _memoryOptions?.Scoring;
        if (scoring != null)
        {
            var weightSum = scoring.SemanticWeight + scoring.RecencyWeight + scoring.ImportanceBoostWeight;
            if (Math.Abs(weightSum - 1.0) > 0.01)
            {
                _logger.LogWarning(
                    "Memory scoring weights do not sum to 1.0 (actual: {WeightSum}). " +
                    "Semantic={Semantic}, Recency={Recency}, ImportanceBoost={ImportanceBoost}",
                    weightSum, scoring.SemanticWeight, scoring.RecencyWeight, scoring.ImportanceBoostWeight);
            }
        }
    }

    /// <inheritdoc />
    public async Task<string?> ReadAsync(string scope, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);

        var query = _repository.AsQueryable().Where(e => e.Scope == scope);

        // 过期过滤
        if (_memoryOptions?.EntryExpiration.HasValue == true)
        {
            var cutoff = DateTime.UtcNow - _memoryOptions.EntryExpiration.Value;
            query = query.Where(e => e.CreationTime >= cutoff);
        }

        var entries = await query
            .OrderBy(e => e.CreationTime)
            .Select(e => e.Content)
            .ToListAsync(ct);

        return entries.Count > 0 ? string.Join("\n", entries) : null;
    }

    /// <inheritdoc />
    public async Task WriteAsync(string scope, string content, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);
        Check.NotNull(content);

        // 使用事务包装删除+插入操作，确保原子性
        if (_unitOfWorkManager != null)
        {
            await _unitOfWorkManager.BeginTransactionAsync(ct);
            try
            {
                await WriteInternalAsync(scope, content, ct);
                await _unitOfWorkManager.CommitTransactionAsync(ct);
            }
            catch
            {
                await _unitOfWorkManager.RollbackTransactionAsync(ct);
                throw;
            }
        }
        else
        {
            // 回退到非事务行为
            await WriteInternalAsync(scope, content, ct);
        }

        _logger.LogDebug("Written memory for scope {Scope}, length: {Length}", scope, content.Length);
    }

    private async Task WriteInternalAsync(string scope, string content, CancellationToken ct)
    {
        // 删除该 scope 下所有旧条目
        await _repository.AsQueryable()
            .Where(e => e.Scope == scope)
            .ExecuteDeleteAsync(ct);

        // 写入新条目
        var entry = new MemoryEntry
        {
            Scope = scope,
            Content = content,
            Source = "write",
            EmbeddingVector = await TryGenerateEmbeddingAsync(content, ct)
        };
        await _repository.InsertAsync(entry);
    }

    /// <inheritdoc />
    public async Task AppendAsync(string scope, string entry, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);
        Check.NotNullOrWhiteSpace(entry);

        var memoryEntry = new MemoryEntry
        {
            Scope = scope,
            Content = entry,
            Source = "append",
            EmbeddingVector = await TryGenerateEmbeddingAsync(entry, ct)
        };
        await _repository.InsertAsync(memoryEntry);

        _logger.LogDebug("Appended memory for scope {Scope}, length: {Length}", scope, entry.Length);

        // 修剪: 超过最大条目数时删除最旧条目
        await TrimExcessEntriesAsync(scope, ct);
    }

    /// <summary>
    /// 追加带元数据的内容到指定 scope 的记忆
    /// </summary>
    public async Task AppendAsync(string scope, string entry, double importance, string? category, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);
        Check.NotNullOrWhiteSpace(entry);

        var memoryEntry = new MemoryEntry
        {
            Scope = scope,
            Content = entry,
            Source = "append",
            Importance = importance,
            Category = category,
            EmbeddingVector = await TryGenerateEmbeddingAsync(entry, ct)
        };
        await _repository.InsertAsync(memoryEntry);

        _logger.LogDebug("Appended memory for scope {Scope} with importance {Importance}, category {Category}", scope, importance, category);

        await TrimExcessEntriesAsync(scope, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(string scope, string query, int maxResults = 10, CancellationToken ct = default)
        => await SearchInternalAsync(scope, query, maxResults, category: null, ct);

    /// <summary>
    /// 按类别搜索指定 scope 的记忆
    /// </summary>
    public async Task<IReadOnlyList<MemorySearchResult>> SearchByCategoryAsync(
        string scope, string query, string category, int maxResults = 10,
        CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(category);
        return await SearchInternalAsync(scope, query, maxResults, category, ct);
    }

    private async Task<IReadOnlyList<MemorySearchResult>> SearchInternalAsync(
        string scope, string query, int maxResults, string? category, CancellationToken ct)
    {
        Check.NotNullOrWhiteSpace(scope);
        Check.NotNullOrWhiteSpace(query);

        var queryLower = query.ToLower();
        var keywords = queryLower.Contains(' ')
            ? queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [queryLower];

        var baseQuery = _repository.AsQueryable().Where(e => e.Scope == scope);
        if (!string.IsNullOrEmpty(category))
            baseQuery = baseQuery.Where(e => e.Category == category);

        // DB 层预过滤: 至少匹配一个关键词
        var preFiltered = baseQuery
            .Where(e => keywords.Any(k => e.Content.ToLower().Contains(k)))
            .OrderByDescending(e => e.CreationTime);

        var entries = await preFiltered.Take(maxResults * 3).ToListAsync(ct);

        // 预过滤结果为空时回退全量加载（有界）
        if (entries.Count == 0)
        {
            entries = await baseQuery
                .OrderByDescending(e => e.CreationTime)
                .Take(maxResults * 3)
                .ToListAsync(ct);
        }

        if (entries.Count == 0)
        {
            return [];
        }

        // 尝试混合搜索：向量 + 关键词
        float[]? queryVector = null;
        if (_embeddingService != null)
        {
            EmbeddingOptions? embOptions = null;
            if (_memoryOptions is { EmbeddingProvider: not null } or { EmbeddingModel: not null })
            {
                embOptions = new EmbeddingOptions
                {
                    Provider = _memoryOptions.EmbeddingProvider,
                    Model = _memoryOptions.EmbeddingModel
                };
            }

            var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(query, embOptions, ct);
            if (embeddingResult.Succeeded)
            {
                queryVector = embeddingResult.Data;
            }
            else
            {
                _logger.LogDebug("Embedding generation failed, falling back to keyword search: {Message}", embeddingResult.Message);
            }
        }

        // 关键词匹配 + 向量融合评分
        // 缓存循环中的常量值，避免每次迭代重复访问
        var now = DateTime.UtcNow;
        var scoring = _memoryOptions?.Scoring;
        var importanceWeight = _memoryOptions?.ImportanceWeight ?? 1.0;
        var recencyDecayRate = _memoryOptions?.RecencyDecayRate ?? 0;

        var results = new List<MemorySearchResult>();
        foreach (var entry in entries)
        {
            // 关键词得分
            var contentLower = entry.Content.ToLower();
            var matchCount = keywords.Count(k => contentLower.Contains(k));
            var keywordScore = keywords.Length > 0 ? (double)matchCount / keywords.Length : 0;

            // 向量得分（余弦相似度）
            double vectorScore = 0;
            if (queryVector != null && entry.EmbeddingVector != null)
            {
                vectorScore = CosineSimilarity(queryVector, entry.EmbeddingVector);
            }

            // 可配置复合评分公式（CrewAI 风格）
            double finalScore;

            if (scoring != null)
            {
                // 新配置模式：加权求和
                var semanticScore = (queryVector != null && entry.EmbeddingVector != null)
                    ? VectorWeight * vectorScore + KeywordWeight * keywordScore
                    : keywordScore;

                // 时间衰减分量
                double recencyDecay = 1.0;
                var lastAccess = entry.LastAccessedTime ?? entry.CreationTime;
                var daysSince = (now - lastAccess).TotalDays;
                if (scoring.RecencyHalfLifeDays is > 0)
                {
                    // 半衰期公式：exp(-ln2/halfLife * days)
                    recencyDecay = Math.Exp(-Math.Log(2) / scoring.RecencyHalfLifeDays.Value * daysSince);
                }
                else if (recencyDecayRate > 0)
                {
                    // 向后兼容旧配置
                    recencyDecay = Math.Exp(-recencyDecayRate * daysSince);
                }
                var importanceComponent = scoring.BaseScore + entry.Importance * importanceWeight;

                finalScore = scoring.SemanticWeight * semanticScore
                    + scoring.RecencyWeight * recencyDecay
                    + scoring.ImportanceBoostWeight * importanceComponent;
            }
            else
            {
                // 旧模式向后兼容：乘法叠加
                if (queryVector != null && entry.EmbeddingVector != null)
                {
                    finalScore = VectorWeight * vectorScore + KeywordWeight * keywordScore;
                }
                else
                {
                    finalScore = keywordScore;
                }

                var importanceBoost = 0.5 + entry.Importance * importanceWeight;
                finalScore *= importanceBoost;

                if (recencyDecayRate > 0)
                {
                    var lastAccess = entry.LastAccessedTime ?? entry.CreationTime;
                    var daysSince = (now - lastAccess).TotalDays;
                    var decay = Math.Exp(-recencyDecayRate * daysSince);
                    finalScore *= decay;
                }
            }

            if (finalScore > 0)
            {
                results.Add(new MemorySearchResult
                {
                    Id = entry.Id,
                    Content = entry.Content,
                    Source = entry.Source,
                    Category = entry.Category,
                    Score = finalScore
                });
            }
        }

        var finalResults = results
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();

        // 访问追踪：更新命中条目的 LastAccessedTime 和 AccessCount
        if (finalResults.Count > 0)
        {
            try
            {
                var hitIds = finalResults.Where(r => r.Id.HasValue).Select(r => r.Id!.Value).ToList();
                if (hitIds.Count > 0)
                {
                    await _repository.AsQueryable()
                        .Where(e => hitIds.Contains(e.Id))
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(e => e.LastAccessedTime, DateTime.UtcNow)
                            .SetProperty(e => e.AccessCount, e => e.AccessCount + 1), ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update access tracking for search results");
            }
        }

        return finalResults;
    }

    /// <inheritdoc />
    public async Task ClearAsync(string scope, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);

        var count = await _repository.AsQueryable()
            .Where(e => e.Scope == scope)
            .ExecuteDeleteAsync(ct);

        if (count > 0)
        {
            _logger.LogDebug("Cleared {Count} memory entries for scope {Scope}", count, scope);
        }
    }

    /// <summary>
    /// 按 ID 更新指定条目的内容
    /// </summary>
    public async Task UpdateEntryAsync(string scope, Guid entryId, string newContent, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);
        Check.NotNull(newContent);

        var entry = await _repository.GetAsync(entryId, ct);
        if (entry == null || entry.Scope != scope)
        {
            _logger.LogWarning("UpdateEntryAsync: entry {Id} not found in scope {Scope}", entryId, scope);
            return;
        }

        entry.Content = newContent;
        entry.EmbeddingVector = await TryGenerateEmbeddingAsync(newContent, ct);
        await _repository.UpdateAsync(entry);
        _logger.LogDebug("Updated memory entry {Id} in scope {Scope}", entryId, scope);
    }

    /// <summary>
    /// 按 ID 删除指定条目
    /// </summary>
    public async Task DeleteEntryAsync(string scope, Guid entryId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);

        var entry = await _repository.GetAsync(entryId, ct);
        if (entry == null || entry.Scope != scope)
        {
            _logger.LogWarning("DeleteEntryAsync: entry {Id} not found in scope {Scope}", entryId, scope);
            return;
        }

        await _repository.DeleteAsync(entry);
        _logger.LogDebug("Deleted memory entry {Id} from scope {Scope}", entryId, scope);
    }

    // --- MemoryScope 重载：写入 UserId/AgentId ---

    /// <summary>
    /// 追加带元数据的内容到指定 scope 的记忆（含用户/Agent 隔离）
    /// </summary>
    public async Task AppendAsync(MemoryScope scope, string entry, double importance, string? category, CancellationToken ct = default)
    {
        Check.NotNull(scope);
        Check.NotNullOrWhiteSpace(entry);

        var scopeKey = scope.ToScopeKey();
        var memoryEntry = new MemoryEntry
        {
            Scope = scopeKey,
            Content = entry,
            Source = "append",
            Importance = importance,
            Category = category,
            UserId = scope.UserId,
            AgentId = scope.AgentId,
            EmbeddingVector = await TryGenerateEmbeddingAsync(entry, ct)
        };
        await _repository.InsertAsync(memoryEntry);

        _logger.LogDebug("Appended memory for scope {Scope} with importance {Importance}, category {Category}", scopeKey, importance, category);

        await TrimExcessEntriesAsync(scopeKey, ct);
    }

    /// <summary>
    /// 写入或替换指定 scope 的记忆内容（含用户/Agent 隔离）
    /// </summary>
    public async Task WriteAsync(MemoryScope scope, string content, CancellationToken ct = default)
    {
        Check.NotNull(scope);
        Check.NotNull(content);

        var scopeKey = scope.ToScopeKey();

        if (_unitOfWorkManager != null)
        {
            await _unitOfWorkManager.BeginTransactionAsync(ct);
            try
            {
                await WriteInternalAsync(scopeKey, content, scope.UserId, scope.AgentId, ct);
                await _unitOfWorkManager.CommitTransactionAsync(ct);
            }
            catch
            {
                await _unitOfWorkManager.RollbackTransactionAsync(ct);
                throw;
            }
        }
        else
        {
            await WriteInternalAsync(scopeKey, content, scope.UserId, scope.AgentId, ct);
        }

        _logger.LogDebug("Written memory for scope {Scope}, length: {Length}", scopeKey, content.Length);
    }

    /// <summary>
    /// 追加内容到指定 scope 的记忆（含用户/Agent 隔离）
    /// </summary>
    public async Task AppendAsync(MemoryScope scope, string entry, CancellationToken ct = default)
    {
        Check.NotNull(scope);
        Check.NotNullOrWhiteSpace(entry);

        var scopeKey = scope.ToScopeKey();

        var memoryEntry = new MemoryEntry
        {
            Scope = scopeKey,
            Content = entry,
            Source = "append",
            UserId = scope.UserId,
            AgentId = scope.AgentId,
            EmbeddingVector = await TryGenerateEmbeddingAsync(entry, ct)
        };
        await _repository.InsertAsync(memoryEntry);

        _logger.LogDebug("Appended memory for scope {Scope}, length: {Length}", scopeKey, entry.Length);

        // 修剪: 超过最大条目数时删除最旧条目
        await TrimExcessEntriesAsync(scopeKey, ct);
    }

    private async Task WriteInternalAsync(string scope, string content, Guid? userId, Guid? agentId, CancellationToken ct)
    {
        // 删除该 scope 下所有旧条目
        await _repository.AsQueryable()
            .Where(e => e.Scope == scope)
            .ExecuteDeleteAsync(ct);

        // 写入新条目
        var entry = new MemoryEntry
        {
            Scope = scope,
            Content = content,
            Source = "write",
            UserId = userId,
            AgentId = agentId,
            EmbeddingVector = await TryGenerateEmbeddingAsync(content, ct)
        };
        await _repository.InsertAsync(entry);
    }

    /// <summary>
    /// 修剪超出限制的最旧条目
    /// </summary>
    private async Task TrimExcessEntriesAsync(string scopeKey, CancellationToken ct)
    {
        if (_memoryOptions?.MaxEntriesPerScope is not > 0)
        {
            return;
        }

        try
        {
            var max = _memoryOptions.MaxEntriesPerScope.Value;
            var totalCount = await _repository.AsQueryable()
                .Where(e => e.Scope == scopeKey)
                .CountAsync(ct);

            if (totalCount <= max) return;

            var excessCount = totalCount - max;
            var idsToDelete = await _repository.AsQueryable()
                .Where(e => e.Scope == scopeKey)
                .OrderBy(e => e.CreationTime)
                .Take(excessCount)
                .Select(e => e.Id)
                .ToListAsync(ct);

            if (idsToDelete.Count > 0)
            {
                await _repository.AsQueryable()
                    .Where(e => idsToDelete.Contains(e.Id))
                    .ExecuteDeleteAsync(ct);
                _logger.LogDebug("Trimmed {Count} excess memory entries for scope {Scope}", idsToDelete.Count, scopeKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to trim excess entries for scope {Scope}", scopeKey);
        }
    }

    /// <summary>
    /// 尝试为内容生成嵌入向量，失败时返回 null（不阻断主流程）
    /// </summary>
    private async Task<float[]?> TryGenerateEmbeddingAsync(string content, CancellationToken ct)
    {
        if (_embeddingService == null || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            // 使用 Memory 配置的 Embedding Provider/Model（避免回退到不支持 Embedding 的默认 Provider）
            EmbeddingOptions? options = null;
            if (_memoryOptions is { EmbeddingProvider: not null } or { EmbeddingModel: not null })
            {
                options = new EmbeddingOptions
                {
                    Provider = _memoryOptions.EmbeddingProvider,
                    Model = _memoryOptions.EmbeddingModel
                };
            }

            var result = await _embeddingService.GenerateEmbeddingAsync(content, options, ct);
            return result.Succeeded ? result.Data : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to generate embedding for memory content, continuing without vector");
            return null;
        }
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

    // --- 静态辅助方法：去重 / 清洗 / 裁剪 ---

    [GeneratedRegex(@"<uploaded_files>[\s\S]*?</uploaded_files>", RegexOptions.IgnoreCase)]
    private static partial Regex UploadBlockRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceCollapseRegex();

    /// <summary>
    /// 正规化内容用于去重比较 — 折叠空白字符
    /// </summary>
    public static string NormalizeForDedup(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";
        return WhitespaceCollapseRegex().Replace(content.Trim(), " ");
    }

    /// <summary>
    /// 清除 &lt;uploaded_files&gt; 块（避免将临时文件元数据持久化到记忆中）
    /// </summary>
    public static string ScrubUploadMentions(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        return UploadBlockRegex().Replace(content, "").Trim();
    }

    /// <summary>
    /// 判断两段内容在空白正规化后是否重复
    /// </summary>
    public static bool IsDuplicate(string existing, string incoming)
        => string.Equals(NormalizeForDedup(existing), NormalizeForDedup(incoming), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 按置信度降序排序，裁剪低于阈值或超过 MaxFacts 的条目
    /// </summary>
    public static List<(string Content, double Confidence)> PruneByConfidence(
        List<(string Content, double Confidence)> entries,
        int maxFacts,
        double confidenceThreshold)
    {
        return entries
            .Where(e => e.Confidence >= confidenceThreshold)
            .OrderByDescending(e => e.Confidence)
            .Take(maxFacts)
            .ToList();
    }
}
