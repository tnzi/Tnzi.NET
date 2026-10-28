namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 基于向量的文本搜索服务 - 替换 NoOpTextSearchService，
/// 为 Agent 的 TextSearchProvider 提供 RAG 检索能力
/// </summary>
/// <remarks>
/// <para>
/// 委托给 <see cref="IRagRetriever"/>（查询改写 → 嵌入 → 向量搜索 → 图谱搜索 → 重排序 → 后处理 → 相关性评分 →
/// 父文档窗口 → 图谱片段），再映射为 <see cref="TextSearchResult"/>。
/// ★ 此前这里自带一条只到「相关性评分」为止的精简管线：GraphRAG 与 Parent Document Retrieval 只长在
/// <c>RagRetriever</c> 里，而它的消费者只有用户直连的两个引擎 —— agent 对话（主路径）从来没拿到过图谱片段或父窗口，
/// 运营方却已经为每份文档多付了一次图谱抽取。一条管线，两个出口。
/// </para>
/// <para>
/// 检索范围只来自 <see cref="TextSearchFilter.KnowledgeBaseIds"/>（Agent 的知识库授权经 TextSearchProvider 传入），
/// 逐库查询后合并重排。<b>没有范围就答空</b>：不带过滤的重载、空范围、以及带了本实现表达不了的维度
/// （用户 / agent / 线程 / 集合 / 元数据）的过滤一律不检索，判据见 <see cref="RetrievalAugmentation.ResolveKnowledgeBaseScope"/>。
/// 不套 <c>MinRelevance</c> 阈值（与改造前一致）：agent 上下文注入的相关性由重排与评分器决定。
/// </para>
/// </remarks>
public class VectorTextSearchService : ApplicationService, ITextSearchService
{
    private readonly IRagRetriever _retriever;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;

    public VectorTextSearchService(
        IServiceProvider serviceProvider,
        IRagRetriever retriever,
        IRepository<KnowledgeDocument, Guid> docRepository) : base(serviceProvider)
    {
        _retriever = Check.NotNull(retriever);
        _docRepository = Check.NotNull(docRepository);
    }

    /// <inheritdoc />
    /// <remarks>不带过滤 = 没有知识库范围，恒答空（见类注释）。</remarks>
    public Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken ct = default)
        => SearchAsync(query, filter: null, maxResults, ct);

    /// <inheritdoc />
    public Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query,
        TextSearchFilter? filter,
        int maxResults = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IEnumerable<TextSearchResult>>([]);
        }

        var knowledgeBaseIds = RetrievalAugmentation.ResolveKnowledgeBaseScope(filter);
        if (knowledgeBaseIds is null)
        {
            Logger.LogDebug("VectorTextSearchService skipped a search without a knowledge base scope it can enforce");
            return Task.FromResult<IEnumerable<TextSearchResult>>([]);
        }

        return SearchCoreAsync(query, knowledgeBaseIds, maxResults, ct);
    }

    private async Task<IEnumerable<TextSearchResult>> SearchCoreAsync(
        string query,
        IReadOnlyList<Guid> knowledgeBaseIds,
        int maxResults,
        CancellationToken ct)
    {
        try
        {
            var options = new RagRetrievalOptions
            {
                KnowledgeBaseIds = knowledgeBaseIds.ToList(),
                TopK = maxResults,
                MinRelevance = 0
            };

            var results = await _retriever.RetrieveAsync(query, options, ct);
            if (results.Count == 0)
            {
                return [];
            }

            var docIds = results.Select(r => r.DocumentId).Where(id => id != Guid.Empty).Distinct().ToList();
            var docs = docIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _docRepository.AsQueryable()
                    .Where(d => docIds.Contains(d.Id))
                    .ToDictionaryAsync(d => d.Id, d => d.FileName, ct);

            var searchResults = RetrievalAugmentation.ToTextSearchResults(results, docs);

            Logger.LogDebug("VectorTextSearchService returned {Count} results for query length {Length} (kbScope={KbScope})",
                searchResults.Count, query.Length, knowledgeBaseIds.Count);

            return searchResults;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Vector text search failed for query length {Length}", query.Length);
            return [];
        }
    }
}
