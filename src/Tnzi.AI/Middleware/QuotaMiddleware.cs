namespace Tnzi.AI.Middleware;

/// <summary>
/// 配额中间件：USD 预算门 + 按用户的用量上限（一轮开始前判上限、开窗口，跑完按实际费用结算）。
/// </summary>
/// <remarks>
/// <para>
/// 用量上限按 API 等价费用计：这一轮各次模型调用的 token（含缓存读写）按 <c>AI:CostTracking</c> 配的单价折算。
/// 单价没配的模型算不出成本，这一轮记 0 并记一条 Warning：上限对它拦不住任何东西，这件事要在日志里看得见。
/// </para>
/// <para>
/// 一轮开始时没超、跑的过程中超了，照常跑完、照常记账，下一轮才拦下。外部 CLI 路径不经这条管线，
/// 同一对 <see cref="IUsageLimitService.BeginAsync"/> / <see cref="IUsageLimitService.SettleAsync"/> 在它的执行器里调用。
/// </para>
/// </remarks>
public class QuotaMiddleware : IAiMiddleware
{
    private readonly IUsageLimitService _usageLimits;
    private readonly IBudgetService _budgetService;
    private readonly ICostCalculator? _costCalculator;
    private readonly ILogger<QuotaMiddleware> _logger;

    public int Order => AiMiddlewareOrders.Quota;

    public QuotaMiddleware(
        IUsageLimitService usageLimits,
        IBudgetService budgetService,
        ILogger<QuotaMiddleware> logger,
        ICostCalculator? costCalculator = null)
    {
        _usageLimits = Check.NotNull(usageLimits);
        _budgetService = Check.NotNull(budgetService);
        _logger = Check.NotNull(logger);
        _costCalculator = costCalculator;
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        // 无用户 ID 时跳过（后台任务、匿名调用不计到任何人头上）
        if (context.Request.UserId is not { } userId)
        {
            return await next(context, cancellationToken);
        }

        // USD 预算管控：在开窗口之前查，注定被拒的一轮不该先开出一个窗口
        var budgetResult = await CheckBudgetAsync(context, userId, cancellationToken);
        if (budgetResult != null)
        {
            return budgetResult;
        }

        var begun = await _usageLimits.BeginAsync(userId, cancellationToken);
        if (!begun.Succeeded)
        {
            return BuildRejection(userId, begun);
        }

        var reservation = begun.Data!;
        AgentRunResult? result = null;
        try
        {
            result = await next(context, cancellationToken);
            return result;
        }
        finally
        {
            // 无论成功、失败还是取消都结算一次（已经花掉的部分照记）；用 None：令牌可能已取消
            await SettleAsync(context, reservation, result?.Usage, result?.Model);
        }
    }

    public IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        // 无用户 ID 时跳过
        if (context.Request.UserId is not { } userId)
        {
            return next(context, cancellationToken);
        }

        return InvokeStreamingCoreAsync(context, next, userId, cancellationToken);
    }

    private async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingCoreAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, Guid userId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var budgetCheck = await _budgetService.CheckBudgetAsync(userId, ResolveTenantId(context), context.Agent.AgentId, cancellationToken);
        if (!budgetCheck.IsAllowed)
        {
            _logger.LogWarning("Budget exceeded for user {UserId}: {Reason}", userId, budgetCheck.Reason);
            yield return new AgentStreamChunk
            {
                Text = budgetCheck.Reason ?? "Budget exceeded",
                FinishReason = FinishReasons.QuotaExceeded
            };
            yield break;
        }

        var begun = await _usageLimits.BeginAsync(userId, cancellationToken);
        if (!begun.Succeeded)
        {
            var rejection = BuildRejection(userId, begun);
            // 以错误块交出（不是正文）：客户端据错误码显示用量上限提示，而不是把拒绝原因当成回答的一部分
            yield return new AgentStreamChunk
            {
                Error = rejection.Response,
                ErrorCode = rejection.ErrorCode,
                FinishReason = rejection.FinishReason,
                UsageLimitResetsAt = rejection.UsageLimitResetsAt,
                UsageLimitWindow = rejection.UsageLimitWindow
            };
            yield break;
        }

        var reservation = begun.Data!;

        // 每个 chunk 的 Usage 是一次 LLM 调用的增量（StreamUsage 口径），结算的是全部之和
        TokenUsageDto? totalUsage = null;
        string? model = null;

        try
        {
            await foreach (var chunk in next(context, cancellationToken))
            {
                totalUsage = StreamUsage.Add(totalUsage, chunk.Usage);
                if (!string.IsNullOrWhiteSpace(chunk.Model))
                {
                    model = chunk.Model;
                }

                yield return chunk; // 立即转发，保持真正的流式延迟
            }
        }
        finally
        {
            await SettleAsync(context, reservation, totalUsage, model);
        }
    }

    /// <summary>
    /// 按这一轮实际的用量结算。算不出成本（没有用量、或这个模型没配单价）时记 0：
    /// 前者确实没花钱，后者花了钱却量不出来，所以后者记一条 Warning，上限对这类轮次拦不住任何东西。
    /// </summary>
    /// <remarks>结算失败只记日志不抛：它跑在 finally 里，抛出会盖掉这一轮本身的结局。</remarks>
    private async Task SettleAsync(AiMiddlewareContext context, UsageLimitReservation reservation, TokenUsageDto? usage, string? reportedModel)
    {
        var provider = context.Agent.Provider;
        var model = string.IsNullOrWhiteSpace(reportedModel) ? context.Agent.Model ?? string.Empty : reportedModel;
        var cost = 0m;

        if (usage is { } used && used.InputTokens + used.OutputTokens > 0)
        {
            var calculated = _costCalculator?.CalculateCost(
                provider, model, used.InputTokens, used.OutputTokens, used.CachedInputTokens, used.CacheCreationTokens);
            if (calculated is { } value)
            {
                cost = value;
            }
            else
            {
                _logger.LogWarning(
                    "No cost rate for {Provider}/{Model}; this turn of user {UserId} ({Input} in / {Output} out tokens) does not count toward the usage limits",
                    provider, model, reservation.UserId, used.InputTokens, used.OutputTokens);
            }
        }

        try
        {
            var settled = await _usageLimits.SettleAsync(reservation, cost, CancellationToken.None);
            if (!settled.Succeeded)
            {
                _logger.LogWarning("Failed to record the usage of user {UserId}: {Message}", reservation.UserId, settled.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record the usage of user {UserId}", reservation.UserId);
        }
    }

    /// <summary>检查 USD 预算，超限时返回拒绝结果，否则返回 null</summary>
    private async Task<AgentRunResult?> CheckBudgetAsync(AiMiddlewareContext context, Guid userId, CancellationToken ct)
    {
        var budgetCheck = await _budgetService.CheckBudgetAsync(userId, ResolveTenantId(context), context.Agent.AgentId, ct);
        if (budgetCheck.IsAllowed) return null;

        _logger.LogWarning("Budget exceeded for user {UserId}: {Reason}", userId, budgetCheck.Reason);
        return new AgentRunResult
        {
            Response = budgetCheck.Reason ?? "Budget exceeded",
            FinishReason = FinishReasons.QuotaExceeded
        };
    }

    /// <summary>
    /// 开窗口失败的结果：到上限（429 <c>AI_USAGE_LIMIT_REACHED</c>，带窗口与重置时间）以结束原因 <c>quota_exceeded</c> 交回；
    /// 计量子系统自身的失败如实以 <c>error</c> 交回、原样带着它的错误码（不说成「到上限了」，那会把排查引到上限配置上去）。
    /// </summary>
    private AgentRunResult BuildRejection(Guid userId, Result<UsageLimitReservation> begun)
    {
        if (string.Equals(begun.ErrorCode, ErrorCodes.UsageLimitReached, StringComparison.Ordinal) || begun.Code == 429)
        {
            var denial = begun.ErrorDetails as UsageLimitDenialDto;
            _logger.LogInformation("Usage limit reached for user {UserId} ({Window}, resets at {ResetsAt})",
                userId, denial?.Window, denial?.ResetsAt);
            return new AgentRunResult
            {
                Response = begun.Message ?? "Usage limit reached.",
                FinishReason = FinishReasons.QuotaExceeded,
                // 归一化为框架码：可替换实现可能只给了 429 不给码
                ErrorCode = ErrorCodes.UsageLimitReached,
                UsageLimitResetsAt = denial?.ResetsAt,
                UsageLimitWindow = denial?.Window
            };
        }

        _logger.LogError("Usage limit check could not be completed for user {UserId} ({ErrorCode}): {Error}",
            userId, begun.ErrorCode, begun.Message);
        return new AgentRunResult
        {
            Response = begun.Message ?? "The usage limit could not be checked.",
            FinishReason = FinishReasons.Error,
            ErrorCode = begun.ErrorCode ?? ErrorCodes.UsageLimitCheckFailed
        };
    }

    /// <summary>从中间件上下文解析租户 ID</summary>
    private static Guid? ResolveTenantId(AiMiddlewareContext context)
    {
        var currentTenant = context.ServiceProvider.GetService<ICurrentTenant>();
        return currentTenant?.Id;
    }
}
