namespace Tnzi.AI.Infrastructure.Memory;

/// <summary>
/// 数据库记忆存储 — 使用 EF Core 持久化记忆
/// </summary>
/// <remarks>
/// 支持混合搜索模式：当 IEmbeddingService 可用时，使用 70% 向量相似度 + 30% 关键词匹配的融合评分；
/// 否则降级为纯关键词搜索（向后兼容）。
/// </remarks>
public class DatabaseMemoryStore : IMemoryStore
{
    private readonly IRepository<MemoryEntry, Guid> _repository;
    private readonly IUnitOfWorkManager? _unitOfWorkManager;
    private readonly IEmbeddingService? _embeddingService;
    private readonly ILogger<DatabaseMemoryStore> _logger;

    private const double VectorWeight = 0.7;
    private const double KeywordWeight = 0.3;

    public DatabaseMemoryStore(
        IRepository<MemoryEntry, Guid> repository,
        ILogger<DatabaseMemoryStore> logger,
        IUnitOfWorkManager? unitOfWorkManager = null,
        IEmbeddingService? embeddingService = null)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
        _unitOfWorkManager = unitOfWorkManager;
        _embeddingService = embeddingService;
    }

    /// <inheritdoc />
    public async Task<string?> ReadAsync(string scope, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);

        var entries = await _repository.AsQueryable()
            .Where(e => e.Scope == scope)
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
        var existing = await _repository.AsQueryable()
            .Where(e => e.Scope == scope)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            await _repository.DeleteManyAsync(existing);
        }

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
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(string scope, string query, int maxResults = 10, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);
        Check.NotNullOrWhiteSpace(query);

        var entries = await _repository.AsQueryable()
            .Where(e => e.Scope == scope)
            .OrderByDescending(e => e.CreationTime)
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            return [];
        }

        // 尝试混合搜索：向量 + 关键词
        float[]? queryVector = null;
        if (_embeddingService != null)
        {
            var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(query, ct: ct);
            if (embeddingResult.Succeeded)
            {
                queryVector = embeddingResult.Data;
            }
            else
            {
                _logger.LogDebug("Embedding generation failed, falling back to keyword search: {Message}", embeddingResult.Message);
            }
        }

        // 关键词匹配搜索
        var queryLower = query.ToLower();
        var keywords = queryLower.Contains(' ')
            ? queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [queryLower];

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

            // 融合评分
            double finalScore;
            if (queryVector != null && entry.EmbeddingVector != null)
            {
                // 混合模式：70% 向量 + 30% 关键词
                finalScore = VectorWeight * vectorScore + KeywordWeight * keywordScore;
            }
            else
            {
                // 纯关键词模式
                finalScore = keywordScore;
            }

            if (finalScore > 0)
            {
                results.Add(new MemorySearchResult
                {
                    Content = entry.Content,
                    Source = entry.Source,
                    Score = finalScore
                });
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();
    }

    /// <inheritdoc />
    public async Task ClearAsync(string scope, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);

        var entries = await _repository.AsQueryable()
            .Where(e => e.Scope == scope)
            .ToListAsync(ct);

        if (entries.Count > 0)
        {
            await _repository.DeleteManyAsync(entries);
            _logger.LogDebug("Cleared {Count} memory entries for scope {Scope}", entries.Count, scope);
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
            var result = await _embeddingService.GenerateEmbeddingAsync(content, ct: ct);
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

        double dotProduct = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator > 0 ? dotProduct / denominator : 0;
    }
}
