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
/// 支持按知识库范围过滤：当 <see cref="TextSearchFilter.KnowledgeBaseIds"/> 非空时，检索仅限
/// 这些知识库（逐库查询后合并重排）；为空时跨所有启用知识库（向后兼容）。
/// 该范围由 Agent 的 <c>KnowledgeBaseIds</c> 分配经 TextSearchProvider 传入。
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
    public Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken ct = default)
        => SearchCoreAsync(query, knowledgeBaseIds: null, maxResults, ct);

    /// <inheritdoc />
    public Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query,
        TextSearchFilter? filter,
        int maxResults = 5,
        CancellationToken ct = default)
        => SearchCoreAsync(query, filter?.KnowledgeBaseIds, maxResults, ct);

    private async Task<IEnumerable<TextSearchResult>> SearchCoreAsync(
        string query,
        IReadOnlyList<Guid>? knowledgeBaseIds,
        int maxResults,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            var options = new RagRetrievalOptions
            {
                KnowledgeBaseIds = knowledgeBaseIds is { Count: > 0 } ? knowledgeBaseIds.Distinct().ToList() : null,
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
                searchResults.Count, query.Length, knowledgeBaseIds is { Count: > 0 } ? knowledgeBaseIds.Count : 0);

            return searchResults;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Vector text search failed for query length {Length}", query.Length);
            return [];
        }
    }
}
