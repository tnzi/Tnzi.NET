namespace Tnzi.AI.Rag.Services;

/// <summary>
/// RAG 查询引擎 - 单轮 Q&amp;A，无历史上下文
/// <para>
/// 流程：IRagRetriever.RetrieveAsync → 格式化上下文 → 单次 IAiUtility 调用 → 返回回答 + 引用。
/// 不维护对话历史，不经过 Agent 中间件管道。适合独立问答和搜索增强场景。
/// </para>
/// </summary>
public class RagQueryEngine : ApplicationService, IRagQueryEngine
{
    private readonly IRagRetriever _retriever;
    private readonly IAiUtility _aiUtility;
    private readonly IRagAccessAuthorizer _authorizer;

    /// <summary>
    /// RAG 回答的输出上限。<c>IAiUtility</c> 的全局默认是为标题生成这类极短输出定的，
    /// 拿它生成一段带引用的回答会被<b>静默截断</b>（不报错，只是话没说完），
    /// 所以这里显式传，不依赖默认值。
    /// </summary>
    private const int AnswerMaxTokens = 2048;

    public RagQueryEngine(
        IServiceProvider serviceProvider,
        IRagRetriever retriever,
        IAiUtility aiUtility,
        IRagAccessAuthorizer authorizer) : base(serviceProvider)
    {
        _retriever = Check.NotNull(retriever);
        _aiUtility = Check.NotNull(aiUtility);
        _authorizer = Check.NotNull(authorizer);
    }

    /// <inheritdoc />
    public async Task<Result<RagQueryResult>> QueryAsync(RagQueryRequest request, CancellationToken ct = default)
    {
        Check.NotNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Fail<RagQueryResult>("Query cannot be empty", 400);
        }

        // 0. 知识库级授权：把请求想查的集合收敛成调用者确实被允许查的那部分。
        //    普通用户永远拿不到 search-all（不带 id 的请求会被换成"允许的这批"）。
        var authorized = await _authorizer.AuthorizeQueryAsync(request.KnowledgeBaseIds, ct);
        if (!authorized.Succeeded)
        {
            return Fail<RagQueryResult>(authorized.Message ?? "Access denied", authorized.Code ?? 403, authorized.ErrorCode);
        }

        // 1. 检索相关文档
        var retrievalOptions = new RagRetrievalOptions
        {
            KnowledgeBaseIds = authorized.Data!.ToList(),
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
        var answer = await _aiUtility.ExecuteAsync(
            systemPrompt,
            request.Query,
            new AiUtilityCallOptions { MaxTokens = AnswerMaxTokens },
            ct);

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
