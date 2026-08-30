namespace Tnzi.AI.Middleware;

/// <summary>
/// 配额中间件 - Before: 预留配额，After: 结算实际用量
/// </summary>
public class QuotaMiddleware : IAiMiddleware
{
    private readonly IQuotaService _quotaService;
    private readonly IBudgetService _budgetService;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<QuotaMiddleware> _logger;

    public int Order => AiMiddlewareOrders.Quota;

    public QuotaMiddleware(
        IQuotaService quotaService,
        IBudgetService budgetService,
        ITokenEstimator tokenEstimator,
        ILogger<QuotaMiddleware> logger,
        IEventBus? eventBus = null)
    {
        _quotaService = Check.NotNull(quotaService);
        _budgetService = Check.NotNull(budgetService);
        _tokenEstimator = Check.NotNull(tokenEstimator);
        _logger = Check.NotNull(logger);
        _eventBus = eventBus;
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        var userId = context.Request.UserId;
        var inputText = context.Request.UserMessage ?? string.Empty;

        // 无用户 ID 时跳过配额检查
        if (userId == null)
        {
            return await next(context, cancellationToken);
        }

        // Budget check: USD 预算管控（在 token 配额之前检查，避免不必要的 token 预留）
        var budgetResult = await CheckBudgetAsync(context, cancellationToken);
        if (budgetResult != null)
        {
            return budgetResult;
        }

        // Before: 预留配额
        var estimatedTokens = _tokenEstimator.Estimate(inputText);
        var reserveResult = await _quotaService.ReserveQuotaAsync(userId.Value, estimatedTokens, cancellationToken);

        if (!reserveResult.Succeeded)
        {
            // 只有真的超限才叫超限：配额记录缺失 / 并发冲突走的是另一条 finish reason，
            // 也不该污染 QuotaExceededEvent（那条事件喂告警与用量分析）。
            if (!IsQuotaExceeded(reserveResult))
            {
                return BuildQuotaFailureResult(userId.Value, reserveResult);
            }

            _logger.LogWarning("Quota reservation failed for user {UserId}: {Error}", userId, reserveResult.Message);
            await PublishQuotaExceededEventAsync(userId.Value, estimatedTokens, reserveResult.Message ?? "Quota exceeded");
            return new AgentRunResult
            {
                Response = reserveResult.Message ?? "Quota exceeded",
                FinishReason = FinishReasons.QuotaExceeded,
                // 归一化为框架码：可替换实现可能只给了 429 不给码
                ErrorCode = ErrorCodes.QuotaExceeded
            };
        }

        var reservation = reserveResult.Data!;

        try
        {
            // 执行下游管道
            var result = await next(context, cancellationToken);

            // After: 结算实际用量
            var actualTokens = (result.Usage?.InputTokens ?? 0) + (result.Usage?.OutputTokens ?? 0);
            await _quotaService.SettleQuotaAsync(userId.Value, reservation, actualTokens, cancellationToken);

            return result;
        }
        catch (Exception)
        {
            // 异常时也结算（使用预估值）；用 CancellationToken.None 避免 token 已取消导致结算失败
            await _quotaService.SettleQuotaAsync(userId.Value, reservation, estimatedTokens, CancellationToken.None);
            throw;
        }
    }

    public IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        var userId = context.Request.UserId;

        // 无用户 ID 时跳过配额检查
        if (userId == null)
        {
            return next(context, cancellationToken);
        }

        return InvokeStreamingCoreAsync(context, next, userId.Value, cancellationToken);
    }

    private async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingCoreAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, Guid userId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var inputText = context.Request.UserMessage ?? string.Empty;

        // Budget check: USD 预算管控
        var budgetCheck = await _budgetService.CheckBudgetAsync(
            userId,
            ResolveTenantId(context),
            context.Agent.AgentId,
            cancellationToken);

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

        // Before: 预留配额
        var estimatedTokens = _tokenEstimator.Estimate(inputText);
        var reserveResult = await _quotaService.ReserveQuotaAsync(userId, estimatedTokens, cancellationToken);

        if (!reserveResult.Succeeded)
        {
            // 与非流式路径同一判据：配额记录缺失 / 并发冲突不是超限（见 IsQuotaExceeded）
            if (!IsQuotaExceeded(reserveResult))
            {
                var failure = BuildQuotaFailureResult(userId, reserveResult);
                yield return new AgentStreamChunk
                {
                    Text = failure.Response,
                    FinishReason = failure.FinishReason
                };
                yield break;
            }

            _logger.LogWarning("Quota reservation failed for user {UserId}: {Error}", userId, reserveResult.Message);
            await PublishQuotaExceededEventAsync(userId, estimatedTokens, reserveResult.Message ?? "Quota exceeded");
            yield return new AgentStreamChunk
            {
                Text = reserveResult.Message ?? "Quota exceeded",
                FinishReason = FinishReasons.QuotaExceeded
            };
            yield break;
        }

        var reservation = reserveResult.Data!;
        TokenUsageDto? lastUsage = null;
        var completedNormally = false;

        try
        {
            await foreach (var chunk in next(context, cancellationToken))
            {
                if (chunk.Usage != null)
                {
                    lastUsage = chunk.Usage;
                }
                yield return chunk; // 立即转发，保持真正的流式延迟
            }
            completedNormally = true;
        }
        finally
        {
            // After: 无论成功或失败都结算配额；用 CancellationToken.None 避免 token 已取消导致结算失败
            try
            {
                if (completedNormally)
                {
                    var actualTokens = (lastUsage?.InputTokens ?? 0) + (lastUsage?.OutputTokens ?? 0);
                    await _quotaService.SettleQuotaAsync(userId, reservation, actualTokens, CancellationToken.None);
                }
                else
                {
                    // 异常/取消时优先使用实际用量，若无则回退到预估值
                    var tokensToSettle = lastUsage != null
                        ? (lastUsage.InputTokens + lastUsage.OutputTokens)
                        : estimatedTokens;
                    await _quotaService.SettleQuotaAsync(userId, reservation, tokensToSettle, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to settle streaming quota for user {UserId}", userId);
            }
        }
    }

    /// <summary>检查 USD 预算，超限时返回拒绝结果，否则返回 null</summary>
    private async Task<AgentRunResult?> CheckBudgetAsync(AiMiddlewareContext context, CancellationToken ct)
    {
        var userId = context.Request.UserId;
        var tenantId = ResolveTenantId(context);
        var agentId = context.Agent.AgentId;

        var budgetCheck = await _budgetService.CheckBudgetAsync(userId, tenantId, agentId, ct);
        if (budgetCheck.IsAllowed) return null;

        _logger.LogWarning("Budget exceeded for user {UserId}: {Reason}", userId, budgetCheck.Reason);
        return new AgentRunResult
        {
            Response = budgetCheck.Reason ?? "Budget exceeded",
            FinishReason = FinishReasons.QuotaExceeded
        };
    }

    /// <summary>
    /// 预留失败是不是「配额真的用完了」。
    /// </summary>
    /// <remarks>
    /// ★ 判据是错误码而不是「预留失败了」。<see cref="IQuotaService.ReserveQuotaAsync"/> 还会因为
    /// <b>配额记录缺失</b>（<c>AI_QUOTA_CHECK_FAILED</c>）与<b>并发冲突</b>
    /// （<c>AI_QUOTA_CONCURRENCY_CONFLICT</c>）而失败，这两种把用户和运维指向的方向完全不同：
    /// 前者要去看那一行为什么不在，后者重试即可，都不该去调额度。全部贴成「配额耗尽」，
    /// 排查会一路走到配额配置上去，而那里什么问题都没有。
    /// <para>
    /// ★ HTTP 429 也算超限：<see cref="IQuotaService"/> 是可替换契约，自定义实现按 HTTP 语义写
    /// <c>Result.Failure("Quota exceeded", 429)</c>（带状态码不带框架错误码）是完全合理的形态。
    /// 只认框架常量会把这类实现的超限贴成通用错误——429 丢了、告警事件也不发。
    /// 内建实现的 429 只可能是超限（缺行 500 / 冲突 409），此判据不会误收。
    /// </para>
    /// </remarks>
    private static bool IsQuotaExceeded(Result<QuotaReservation> reserveResult)
        => string.Equals(reserveResult.ErrorCode, ErrorCodes.QuotaExceeded, StringComparison.Ordinal)
           || reserveResult.Code == 429;

    /// <summary>
    /// 把「配额子系统失败」构造成一条如实的运行结果：finish reason 用 error 而非 quota_exceeded，
    /// 且不发 QuotaExceededEvent（那条事件喂告警与用量分析，混进基础设施故障会让超限统计失真）。
    /// </summary>
    private AgentRunResult BuildQuotaFailureResult(Guid userId, Result<QuotaReservation> reserveResult)
    {
        _logger.LogError(
            "Quota reservation could not be completed for user {UserId} ({ErrorCode}): {Error}",
            userId, reserveResult.ErrorCode, reserveResult.Message);

        return new AgentRunResult
        {
            Response = reserveResult.Message ?? "Quota reservation could not be completed.",
            FinishReason = FinishReasons.Error,
            // 原样携带服务层错误码：并发冲突（AI_QUOTA_CONCURRENCY_CONFLICT）在服务层定的是
            // 「可重试的 409」，丢掉这个码，HTTP 边界只能按 FinishReason=Error 贴 500，
            // 「重试即可」就被翻译成了「内部错误」。
            ErrorCode = reserveResult.ErrorCode
        };
    }

    /// <summary>从中间件上下文解析租户 ID</summary>
    private static Guid? ResolveTenantId(AiMiddlewareContext context)
    {
        var currentTenant = context.ServiceProvider.GetService<ICurrentTenant>();
        return currentTenant?.Id;
    }

    /// <summary>发布配额超限事件（静默失败）</summary>
    private async Task PublishQuotaExceededEventAsync(Guid userId, int estimatedTokens, string reason)
    {
        try
        {
            if (_eventBus == null) return;

            await _eventBus.PublishAsync(new QuotaExceededEvent
            {
                UserId = userId,
                EstimatedTokens = estimatedTokens,
                Reason = reason
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish QuotaExceededEvent for user {UserId}", userId);
        }
    }
}
