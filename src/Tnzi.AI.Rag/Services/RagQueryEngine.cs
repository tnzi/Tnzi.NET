namespace Tnzi.AI.Rag.Services;

/// <summary>
/// RAG 查询引擎 - 单轮 Q&amp;A，无历史上下文
/// <para>
/// 流程：IRagRetriever.RetrieveAsync → 格式化上下文 → 单次 IAiUtility 调用 → 返回回答 + 引用。
/// 不维护对话历史，不经过 Agent 中间件管道。适合独立问答和搜索增强场景。
/// </para>
/// <para>
/// ★ 不经管线不等于不受按用户治理：管线里 <c>QuotaMiddleware</c> 管的两件事（USD 预算门、按用户用量上限的
/// 开窗口与结算）在这里显式做一遍，判据与中间件一致。此前这个端点对已登录用户是一条不计量的模型调用通道：
/// 已经到上限的用户换到 <c>/api/rag/query</c> 就能一直问下去，用量也从不计入他的窗口。
/// </para>
/// <para>
/// <c>IAiUtility</c> 不回报用量：费用按估算的 token 与默认 provider、utility 模型的单价折算（没配单价时记 0）。
/// </para>
/// </summary>
public class RagQueryEngine : ApplicationService, IRagQueryEngine
{
    private readonly IRagRetriever _retriever;
    private readonly IAiUtility _aiUtility;
    private readonly IRagAccessAuthorizer _authorizer;
    private readonly IUsageLimitService _usageLimits;
    private readonly IBudgetService _budgetService;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ICurrentTenant _currentTenant;
    private readonly AIRagOptions _ragOptions;
    private readonly ICostCalculator? _costCalculator;
    private readonly IOptionsMonitor<AIOptions>? _aiOptions;
    private readonly IOptionsMonitor<AiUtilityOptions>? _utilityOptions;

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
        IRagAccessAuthorizer authorizer,
        IUsageLimitService usageLimits,
        IBudgetService budgetService,
        ITokenEstimator tokenEstimator,
        ICurrentTenant currentTenant,
        IOptionsSnapshot<AIRagOptions> ragOptions,
        ICostCalculator? costCalculator = null,
        IOptionsMonitor<AIOptions>? aiOptions = null,
        IOptionsMonitor<AiUtilityOptions>? utilityOptions = null) : base(serviceProvider)
    {
        _retriever = Check.NotNull(retriever);
        _aiUtility = Check.NotNull(aiUtility);
        _authorizer = Check.NotNull(authorizer);
        _usageLimits = Check.NotNull(usageLimits);
        _budgetService = Check.NotNull(budgetService);
        _tokenEstimator = Check.NotNull(tokenEstimator);
        _currentTenant = Check.NotNull(currentTenant);
        _ragOptions = Check.NotNull(ragOptions).Value;
        _costCalculator = costCalculator;
        _aiOptions = aiOptions;
        _utilityOptions = utilityOptions;
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

        // 预算门排在检索之前：注定被拒的请求不该先花掉一次查询向量的嵌入调用
        var userId = CurrentUser?.Id;
        if (userId.HasValue)
        {
            var budget = await _budgetService.CheckBudgetAsync(userId, _currentTenant.Id, agentId: null, ct);
            if (!budget.IsAllowed)
            {
                Logger.LogWarning("RAG query rejected for user {UserId}: {Reason}", userId, budget.Reason);
                return Fail<RagQueryResult>(budget.Reason ?? "Budget exceeded", 429, ErrorCodes.QuotaExceeded);
            }
        }

        // 1. 检索相关文档。TopK 静默 clamp 到 MaxTopK（不用 [Range] 打 400：既有客户端可选传 topK）——
        //    此前只有管理端搜索 clamp，用户端一次 topK=2000000 就把整库分块拼进一条 prompt。
        var retrievalOptions = new RagRetrievalOptions
        {
            KnowledgeBaseIds = authorized.Data!.ToList(),
            TopK = Math.Clamp(request.TopK, 1, _ragOptions.MaxTopK),
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

        // 4. 开调用者的用量窗口（判上限）→ 调用 LLM 生成回答 → 无论结局如何都结算一次
        var inputTokens = _tokenEstimator.Estimate(systemPrompt + "\n" + request.Query);
        UsageLimitReservation? reservation = null;
        if (userId.HasValue)
        {
            var begun = await _usageLimits.BeginAsync(userId.Value, ct);
            if (!begun.Succeeded || begun.Data is null)
            {
                return UsageLimitRejection(userId.Value, begun);
            }

            reservation = begun.Data;
        }

        string? answer = null;
        try
        {
            answer = await _aiUtility.ExecuteAsync(
                systemPrompt,
                request.Query,
                new AiUtilityCallOptions { MaxTokens = AnswerMaxTokens },
                ct);
        }
        finally
        {
            if (reservation is not null)
            {
                // IAiUtility 不回报用量：输入按同一估算器、输出按回答文本估算；没有回答（失败 / 取消）时只算输入
                var outputTokens = string.IsNullOrEmpty(answer) ? 0 : _tokenEstimator.Estimate(answer, baseOverhead: 0);
                await SettleUsageAsync(reservation, EstimateCost(inputTokens, outputTokens));
            }
        }

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
    /// 开用量窗口失败 → 拒绝本次调用（失败关闭），判据与 <c>QuotaMiddleware</c> 一致：
    /// 只有真的到上限（<c>AI_USAGE_LIMIT_REACHED</c> 或 HTTP 429）才答 429 并带上窗口与重置时间；
    /// 计量子系统自身失败原样带出它的状态码与错误码，不贴成「到上限了」。
    /// </summary>
    private Result<RagQueryResult> UsageLimitRejection(Guid userId, Result<UsageLimitReservation> begun)
    {
        var reached = string.Equals(begun.ErrorCode, ErrorCodes.UsageLimitReached, StringComparison.Ordinal)
                      || begun.Code == 429;

        Logger.LogWarning(
            "RAG query rejected: usage limit check for user {UserId} failed ({ErrorCode}): {Message}",
            userId, begun.ErrorCode, begun.Message);

        if (reached)
        {
            return Fail<RagQueryResult>(begun.Message ?? "Usage limit reached.", 429, ErrorCodes.UsageLimitReached, begun.ErrorDetails);
        }

        var statusCode = begun.Code is >= 400 ? begun.Code.Value : 500;
        return Fail<RagQueryResult>(
            begun.Message ?? "The usage limit could not be checked.",
            statusCode,
            begun.ErrorCode ?? ErrorCodes.UsageLimitCheckFailed);
    }

    /// <summary>
    /// 按估算的 token 与默认 provider / utility 模型的单价折算这次回答的费用；没配单价时为 0。
    /// </summary>
    private decimal EstimateCost(long inputTokens, long outputTokens)
    {
        var provider = _aiOptions?.CurrentValue.DefaultProvider ?? string.Empty;
        var model = _utilityOptions?.CurrentValue.Model ?? string.Empty;
        return _costCalculator?.CalculateCost(provider, model, inputTokens, outputTokens, cachedInputTokens: 0, cacheWriteTokens: 0) ?? 0m;
    }

    /// <summary>
    /// 结算这次调用的费用。失败只记日志不抛：它跑在 finally 里，抛出会盖掉调用本身的结局；
    /// 用 <see cref="CancellationToken.None"/>：调用方取消后令牌已失效，带着它去写库结算会当场失败。
    /// </summary>
    private async Task SettleUsageAsync(UsageLimitReservation reservation, decimal costUsd)
    {
        try
        {
            var settled = await _usageLimits.SettleAsync(reservation, costUsd, CancellationToken.None);
            if (!settled.Succeeded)
            {
                Logger.LogWarning("Failed to record the RAG query usage of user {UserId}: {Message}", reservation.UserId, settled.Message);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to record the RAG query usage of user {UserId}", reservation.UserId);
        }
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
