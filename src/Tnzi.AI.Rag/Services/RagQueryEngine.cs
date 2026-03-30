namespace Tnzi.AI.Rag.Services;

/// <summary>
/// RAG 查询引擎 — 单轮 Q&amp;A，无历史上下文
/// <para>
/// 流程：IRagRetriever.RetrieveAsync → 格式化上下文 → 单次 IAiUtility 调用 → 返回回答 + 引用。
/// 不维护对话历史，不经过 Agent 中间件管道。适合独立问答和搜索增强场景。
/// </para>
/// </summary>
public class RagQueryEngine : ApplicationService, IRagQueryEngine
{
    private readonly IRagRetriever _retriever;
    private readonly IAiUtility _aiUtility;

    public RagQueryEngine(
        IServiceProvider serviceProvider,
        IRagRetriever retriever,
        IAiUtility aiUtility) : base(serviceProvider)
    {
        _retriever = Check.NotNull(retriever);
        _aiUtility = Check.NotNull(aiUtility);
    }

    /// <inheritdoc />
    public async Task<Result<RagQueryResult>> QueryAsync(RagQueryRequest request, CancellationToken ct = default)
    {
        Check.NotNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Fail<RagQueryResult>("Query cannot be empty", 400);
        }

        // 1. 检索相关文档
        var retrievalOptions = new RagRetrievalOptions
        {
            KnowledgeBaseIds = request.KnowledgeBaseIds,
            TopK = request.TopK,
            MinRelevance = request.MinRelevance
        };

        var retrievalResults = await _retriever.RetrieveAsync(request.Query, retrievalOptions, ct);

        if (retrievalResults.Count == 0)
        {
            return Ok(new RagQueryResult
            {
                Answer = "No relevant information found in the knowledge base to answer this question."
            });
        }

        // 2. 格式化检索上下文
        var context = FormatRetrievalContext(retrievalResults);

        // 3. 构建系统提示词
        var systemPrompt = BuildSystemPrompt(context);

        // 4. 调用 LLM 生成回答
        var answer = await _aiUtility.ExecuteAsync(systemPrompt, request.Query, cancellationToken: ct);

        if (string.IsNullOrWhiteSpace(answer))
        {
            return Fail<RagQueryResult>("Failed to generate answer from LLM", 500);
        }

        // 5. 构建引用列表
        var citations = request.IncludeCitations
            ? RagRetriever.BuildCitations(retrievalResults)
            : [];

        return Ok(new RagQueryResult
        {
            Answer = answer,
            Citations = citations
        });
    }

    /// <summary>
    /// 将检索结果格式化为 LLM 上下文文本
    /// </summary>
    private static string FormatRetrievalContext(List<RetrievalResult> results)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            sb.AppendLine($"[Source {i + 1}] (relevance: {result.Score:F2})");
            sb.AppendLine(result.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建包含 RAG 上下文的系统提示词
    /// </summary>
    private static string BuildSystemPrompt(string context)
    {
        return $"""
            You are a helpful assistant that answers questions based on the provided context.
            Use ONLY the information from the context below to answer the question.
            If the context doesn't contain enough information to fully answer the question, say so clearly.
            Always cite the source number (e.g., [Source 1]) when referencing information.

            Context:
            {context}
            """;
    }

}
