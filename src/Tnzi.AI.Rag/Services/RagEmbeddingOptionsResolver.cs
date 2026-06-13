namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 嵌入配置解析共享 helper —— query 向量必须与摄取时使用相同的 embedding provider/model，
/// 否则向量空间不一致、检索结果无意义。
/// </summary>
/// <remarks>
/// <para>
/// 解析规则（与 <see cref="DocumentIngestionService"/> 摄取路径完全一致）：
/// KB 的 <c>EmbeddingProvider</c> 为哨兵值 <c>"default"</c>（或空）时回退到
/// <see cref="AIRagOptions.DefaultEmbeddingProvider"/>；<c>EmbeddingModel</c> 为 null 时
/// 回退到 <see cref="AIRagOptions.DefaultEmbeddingModel"/>。
/// </para>
/// <para>
/// 多知识库检索时按解析后的 (provider, model) 分组：同一向量空间的 KB 共享一次 query
/// 嵌入生成，不同空间各自生成（见 <see cref="GroupByEmbeddingConfig"/>）。
/// </para>
/// </remarks>
public static class RagEmbeddingOptionsResolver
{
    /// <summary>KnowledgeBase.EmbeddingProvider 的"使用全局默认"哨兵值。</summary>
    public const string DefaultProviderSentinel = "default";

    /// <summary>
    /// 解析指定知识库的嵌入配置（provider/model 与摄取对齐）。
    /// </summary>
    public static EmbeddingOptions Resolve(KnowledgeBase knowledgeBase, AIRagOptions options)
    {
        Check.NotNull(knowledgeBase);
        Check.NotNull(options);

        var useDefaultProvider = string.IsNullOrWhiteSpace(knowledgeBase.EmbeddingProvider)
            || knowledgeBase.EmbeddingProvider == DefaultProviderSentinel;

        return new EmbeddingOptions
        {
            Provider = useDefaultProvider ? options.DefaultEmbeddingProvider : knowledgeBase.EmbeddingProvider,
            Model = knowledgeBase.EmbeddingModel ?? options.DefaultEmbeddingModel
        };
    }

    /// <summary>
    /// 解析全局默认嵌入配置（跨库 search-all 场景使用）。
    /// </summary>
    public static EmbeddingOptions ResolveDefault(AIRagOptions options)
    {
        Check.NotNull(options);

        return new EmbeddingOptions
        {
            Provider = options.DefaultEmbeddingProvider,
            Model = options.DefaultEmbeddingModel
        };
    }

    /// <summary>
    /// 按解析后的 (provider, model) 对知识库分组 —— 同一嵌入空间的 KB 共用一个 query 向量，
    /// 不同空间逐组生成后分别检索。
    /// </summary>
    public static List<(EmbeddingOptions Options, List<Guid> KnowledgeBaseIds)> GroupByEmbeddingConfig(
        IEnumerable<KnowledgeBase> knowledgeBases, AIRagOptions options)
    {
        Check.NotNull(knowledgeBases);
        Check.NotNull(options);

        return knowledgeBases
            .Select(kb => (KbId: kb.Id, Options: Resolve(kb, options)))
            .GroupBy(x => (x.Options.Provider, x.Options.Model))
            .Select(g => (g.First().Options, g.Select(x => x.KbId).ToList()))
            .ToList();
    }
}
