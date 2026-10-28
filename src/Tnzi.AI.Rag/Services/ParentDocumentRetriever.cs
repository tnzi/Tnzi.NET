namespace Tnzi.AI.Rag.Services;

/// <summary>
/// Parent Document Retriever 实现 - 将细粒度匹配块扩展为更大的上下文窗口
/// <para>
/// 对每个匹配块，加载同一文档中相邻的块（ChunkIndex ± windowSize），
/// 将重叠/相邻的匹配块合并为一个连续的上下文块，减少冗余并提供更完整的上下文。
/// </para>
/// <para>
/// 一次查询只取各窗口里的块（按文档、按合并后的窗口逐个限定）。★ 此前把所有文档、所有命中压成一个全局区间
/// [最小窗口起点, 最大窗口终点]，再对每篇文档都套这个区间：命中文档 A 第 3 块与文档 B 第 650 块，就把两篇的第 1..652 块原文全部读进内存，
/// 只为拼出两个 5 块窗口；同一文档里相距很远的两处命中也一样。它跑在 agent 每一轮检索与用户 RAG 端点上，随文档长度线性放大。
/// </para>
/// </summary>
public class ParentDocumentRetriever : ApplicationService, IParentDocumentRetriever
{
    private readonly IRepository<DocumentChunk, Guid> _chunkRepository;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;

    public ParentDocumentRetriever(
        IServiceProvider serviceProvider,
        IRepository<DocumentChunk, Guid> chunkRepository,
        IRepository<KnowledgeDocument, Guid> docRepository) : base(serviceProvider)
    {
        _chunkRepository = Check.NotNull(chunkRepository);
        _docRepository = Check.NotNull(docRepository);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParentDocumentResult>> RetrieveAsync(
        IReadOnlyList<RetrievalResult> matchedResults,
        ParentRetrievalOptions? options = null,
        CancellationToken ct = default)
    {
        if (matchedResults.Count == 0)
        {
            return [];
        }

        options ??= new ParentRetrievalOptions();

        try
        {
            // 1. 按文档分组匹配结果
            var groupedByDoc = matchedResults
                .Where(r => r.DocumentId != Guid.Empty)
                .GroupBy(r => r.DocumentId)
                .ToList();

            if (groupedByDoc.Count == 0)
            {
                return [];
            }

            // 2. 收集所有匹配块的 ChunkIndex 并计算每个文档需要的索引范围
            var docMatchInfos = new Dictionary<Guid, List<(int ChunkIndex, double Score)>>();
            foreach (var group in groupedByDoc)
            {
                var matchInfos = new List<(int ChunkIndex, double Score)>();
                foreach (var match in group)
                {
                    var chunkIndex = ExtractChunkIndex(match);
                    if (chunkIndex.HasValue)
                    {
                        matchInfos.Add((chunkIndex.Value, match.Score));
                    }
                }
                if (matchInfos.Count > 0)
                {
                    docMatchInfos[group.Key] = matchInfos;
                }
            }

            if (docMatchInfos.Count == 0)
            {
                return [];
            }

            var docIds = docMatchInfos.Keys.ToList();

            // 3. 每篇文档按命中计算窗口并合并重叠 / 相邻的窗口
            var windowsByDoc = docMatchInfos.ToDictionary(
                entry => entry.Key,
                entry => MergeIntervals(ToWindows(entry.Value, options.WindowSize)));

            // 4. 批量加载文档名称 + 各窗口里的块（各一次查询）。
            // 两个查询共用本 scope 的 RagDbContext，必须顺序执行：并发使用同一 DbContext 会抛
            // "A second operation was started on this context" 并被下方 catch 吞成空结果。
            var docNames = await LoadDocumentNamesAsync(docIds, ct);
            var allChunks = await _chunkRepository.AsQueryable()
                .Where(BuildWindowPredicate(windowsByDoc.SelectMany(entry =>
                    entry.Value.Select(window => new ChunkWindow(entry.Key, window.Start, window.End)))))
                .OrderBy(c => c.DocumentId).ThenBy(c => c.ChunkIndex)
                .Select(c => new { c.DocumentId, c.ChunkIndex, c.Content })
                .ToListAsync(ct);

            // 5. 按文档分组块数据
            var chunksByDoc = allChunks
                .GroupBy(c => c.DocumentId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(c => c.ChunkIndex, c => c.Content));

            // 6. 为每个文档构建扩展结果
            var results = new List<ParentDocumentResult>();
            foreach (var (documentId, windows) in windowsByDoc)
            {
                if (!chunksByDoc.TryGetValue(documentId, out var chunkMap) || chunkMap.Count == 0)
                {
                    continue;
                }

                var expandedBlocks = BuildExpandedBlocks(documentId, windows, chunkMap, options);
                foreach (var block in expandedBlocks)
                {
                    docNames.TryGetValue(block.DocumentId, out var docName);
                    results.Add(block with { DocumentName = docName });
                }
            }

            // 7. 按分数降序排列
            results.Sort((a, b) => b.Score.CompareTo(a.Score));

            Logger.LogDebug(
                "Parent document retrieval expanded {MatchCount} matches into {BlockCount} context blocks (window={Window})",
                matchedResults.Count, results.Count, options.WindowSize);

            return results;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Parent document retrieval failed for {Count} matched results", matchedResults.Count);
            return [];
        }
    }

    /// <summary>一篇文档里的一个块窗口（含两端）。</summary>
    internal sealed record ChunkWindow(Guid DocumentId, int Start, int End);

    /// <summary>
    /// 只取各窗口里的块：<c>(DocumentId = d1 AND ChunkIndex BETWEEN s1 AND e1) OR ...</c>。
    /// 窗口值经成员访问进入表达式，EF 把它们作为查询参数而不是内联字面量。窗口数受 TopK 约束（至多一百来个）。
    /// </summary>
    internal static Expression<Func<DocumentChunk, bool>> BuildWindowPredicate(IEnumerable<ChunkWindow> windows)
    {
        var chunk = Expression.Parameter(typeof(DocumentChunk), "c");
        var documentId = Expression.Property(chunk, nameof(DocumentChunk.DocumentId));
        var chunkIndex = Expression.Property(chunk, nameof(DocumentChunk.ChunkIndex));

        Expression? body = null;
        foreach (var window in windows)
        {
            var bounds = Expression.Constant(window);
            var inWindow = Expression.AndAlso(
                Expression.Equal(documentId, Expression.Property(bounds, nameof(ChunkWindow.DocumentId))),
                Expression.AndAlso(
                    Expression.GreaterThanOrEqual(chunkIndex, Expression.Property(bounds, nameof(ChunkWindow.Start))),
                    Expression.LessThanOrEqual(chunkIndex, Expression.Property(bounds, nameof(ChunkWindow.End)))));
            body = body is null ? inWindow : Expression.OrElse(body, inWindow);
        }

        return Expression.Lambda<Func<DocumentChunk, bool>>(body ?? Expression.Constant(false), chunk);
    }

    /// <summary>每个命中块向两侧扩展 <see cref="ParentRetrievalOptions.WindowSize"/> 块（下界截到 0），按起点排序。</summary>
    private static List<(int Start, int End, double Score)> ToWindows(List<(int ChunkIndex, double Score)> matchInfos, int windowSize)
        => matchInfos
            .Select(m => (
                Start: Math.Max(0, m.ChunkIndex - windowSize),
                End: m.ChunkIndex + windowSize,
                m.Score))
            .OrderBy(i => i.Start)
            .ToList();

    /// <summary>
    /// 对单个文档的合并后窗口逐个拼接内容（纯内存操作，不发起 DB 查询）
    /// </summary>
    private static List<ParentDocumentResult> BuildExpandedBlocks(
        Guid documentId,
        List<(int Start, int End, double Score)> mergedIntervals,
        Dictionary<int, string> chunkMap,
        ParentRetrievalOptions options)
    {
        // 为每个合并区间构建结果
        var results = new List<ParentDocumentResult>();
        foreach (var (start, end, score) in mergedIntervals)
        {
            var contentParts = new List<string>();
            var tokenCount = 0;

            for (var i = start; i <= end; i++)
            {
                if (!chunkMap.TryGetValue(i, out var content))
                {
                    continue;
                }

                // 粗略估算 Token 数（每 4 字符约 1 token）
                var estimatedTokens = content.Length / 4;
                if (tokenCount + estimatedTokens > options.MaxTokens && contentParts.Count > 0)
                {
                    break;
                }

                contentParts.Add(content);
                tokenCount += estimatedTokens;
            }

            if (contentParts.Count == 0)
            {
                continue;
            }

            // 实际结束索引可能因 MaxTokens 截断或缺失块而不同
            var actualEnd = start + contentParts.Count - 1;
            // 向前修正：找到实际存在的块的最后一个索引
            var existingIndices = Enumerable.Range(start, end - start + 1)
                .Where(chunkMap.ContainsKey)
                .Take(contentParts.Count)
                .ToList();

            results.Add(new ParentDocumentResult
            {
                DocumentId = documentId,
                StartChunkIndex = existingIndices.Count > 0 ? existingIndices[0] : start,
                EndChunkIndex = existingIndices.Count > 0 ? existingIndices[^1] : actualEnd,
                MergedContent = string.Join("\n", contentParts),
                Score = score
            });
        }

        return results;
    }

    /// <summary>
    /// 合并重叠或相邻的区间，取最大分数
    /// </summary>
    private static List<(int Start, int End, double Score)> MergeIntervals(
        List<(int Start, int End, double Score)> intervals)
    {
        if (intervals.Count == 0) return [];

        var merged = new List<(int Start, int End, double Score)>();
        var current = intervals[0];

        for (var i = 1; i < intervals.Count; i++)
        {
            var next = intervals[i];
            // 相邻或重叠则合并（End + 1 >= Start 表示相邻）
            if (current.End + 1 >= next.Start)
            {
                current = (current.Start, Math.Max(current.End, next.End), Math.Max(current.Score, next.Score));
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    /// <summary>
    /// 从 RetrievalResult 的 metadata 中提取 ChunkIndex
    /// </summary>
    private static int? ExtractChunkIndex(RetrievalResult result)
    {
        if (result.Metadata == null)
        {
            return null;
        }

        if (!result.Metadata.TryGetValue("chunkIndex", out var value))
        {
            return null;
        }

        return value switch
        {
            int intVal => intVal,
            long longVal => (int)longVal,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number =>
                jsonElement.GetInt32(),
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
    }

    /// <summary>
    /// 批量加载文档名称
    /// </summary>
    private async Task<Dictionary<Guid, string>> LoadDocumentNamesAsync(
        List<Guid> docIds, CancellationToken ct)
    {
        var docs = await _docRepository.AsQueryable()
            .Where(d => docIds.Contains(d.Id))
            .Select(d => new { d.Id, d.FileName })
            .ToListAsync(ct);

        return docs.ToDictionary(d => d.Id, d => d.FileName);
    }
}
