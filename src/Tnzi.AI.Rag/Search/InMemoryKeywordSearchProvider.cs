namespace Tnzi.AI.Rag.Search;

/// <summary>
/// 基于内存的关键词搜索提供者 - 使用 TF-IDF 算法进行关键词匹配
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="IRepository{TEntity, TKey}"/> 加载文档块数据进行 TF-IDF 检索。
/// 适用于开发测试和小规模数据场景。生产环境建议使用 <see cref="PgFullTextSearchProvider"/>。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "In-memory keyword search is in preview")]
public class InMemoryKeywordSearchProvider : IKeywordSearchProvider
{
    private readonly IRepository<DocumentChunk, Guid> _chunkRepository;
    private readonly ILogger<InMemoryKeywordSearchProvider> _logger;

    // 分词用的分隔符（空格 + 常见标点）
    private static readonly char[] TokenSeparators =
        [' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '(', ')', '[', ']', '{', '}', '"', '\'', '-', '/'];

    public InMemoryKeywordSearchProvider(
        IRepository<DocumentChunk, Guid> chunkRepository,
        ILogger<InMemoryKeywordSearchProvider> logger)
    {
        _chunkRepository = Check.NotNull(chunkRepository);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<List<KeywordSearchResult>> SearchAsync(
        string query, int topK, Guid? knowledgeBaseId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
        {
            return [];
        }

        var sw = Stopwatch.StartNew();

        // 加载文档块（按知识库过滤）
        var chunksQuery = _chunkRepository.AsQueryable();
        if (knowledgeBaseId.HasValue)
        {
            chunksQuery = chunksQuery.Where(c => c.KnowledgeBaseId == knowledgeBaseId.Value);
        }

        var chunks = await chunksQuery
            .Select(c => new { c.Id, c.Content, c.DocumentId, c.KnowledgeBaseId })
            .ToListAsync(ct);

        if (chunks.Count == 0)
        {
            return [];
        }

        // 构建倒排索引和计算 TF-IDF
        var totalDocuments = chunks.Count;
        var documentFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var documentTokens = new List<(int Index, Dictionary<string, int> TermFrequency)>();

        for (var i = 0; i < chunks.Count; i++)
        {
            var tokens = Tokenize(chunks[i].Content);
            var tf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var token in tokens)
            {
                tf.TryGetValue(token, out var count);
                tf[token] = count + 1;
            }

            // 更新文档频率（每个文档中出现的词只计一次）
            foreach (var term in tf.Keys)
            {
                documentFrequency.TryGetValue(term, out var df);
                documentFrequency[term] = df + 1;
            }

            documentTokens.Add((i, tf));
        }

        // 计算每个文档的 TF-IDF 得分
        var scoredResults = new List<(int Index, double Score)>();

        foreach (var (index, tf) in documentTokens)
        {
            var score = 0.0;

            foreach (var queryToken in queryTokens)
            {
                if (!tf.TryGetValue(queryToken, out var termFreq)) continue;
                if (!documentFrequency.TryGetValue(queryToken, out var docFreq) || docFreq == 0) continue;

                // TF-IDF: tf * log(N / df)
                var tfidf = termFreq * Math.Log((double)totalDocuments / docFreq);
                score += tfidf;
            }

            if (score > 0)
            {
                scoredResults.Add((index, score));
            }
        }

        // 按得分降序排列，取 topK
        var results = scoredResults
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .Select(r => new KeywordSearchResult
            {
                ChunkId = chunks[r.Index].Id,
                Content = chunks[r.Index].Content,
                DocumentId = chunks[r.Index].DocumentId,
                KnowledgeBaseId = chunks[r.Index].KnowledgeBaseId,
                Score = r.Score
            })
            .ToList();

        sw.Stop();
        _logger.LogDebug(
            "InMemory keyword search returned {Count} results from {Total} chunks (topK={TopK}, {Duration:F3}s)",
            results.Count, chunks.Count, topK, sw.Elapsed.TotalSeconds);

        return results;
    }

    /// <summary>
    /// 将文本分词（小写化 + 按分隔符拆分 + 过滤短词）
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .ToLower()
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2) // 过滤单字符词
            .ToList();
    }
}
