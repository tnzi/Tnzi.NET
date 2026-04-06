namespace Tnzi.AI.Middleware;

/// <summary>
/// 配额中间件 — Before: 预留配额，After: 结算实际用量
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
            _logger.LogWarning("Quota reservation failed for user {UserId}: {Error}", userId, reserveResult.Message);
            await PublishQuotaExceededEventAsync(userId.Value, estimatedTokens, reserveResult.Message ?? "Quota exceeded");
            return new AgentRunResult
            {
                Response = reserveResult.Message ?? "Quota exceeded",
                FinishReason = FinishReasons.QuotaExceeded
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
