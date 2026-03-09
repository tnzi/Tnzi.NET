namespace Tnzi.AI.Middleware;

/// <summary>
/// 配额中间件 — Before: 预留配额，After: 结算实际用量
/// </summary>
public class QuotaMiddleware : IAiMiddleware
{
    private readonly IQuotaService _quotaService;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<QuotaMiddleware> _logger;

    public int Order => 100;

    public QuotaMiddleware(
        IQuotaService quotaService,
        ITokenEstimator tokenEstimator,
        ILogger<QuotaMiddleware> logger)
    {
        _quotaService = Check.NotNull(quotaService);
        _tokenEstimator = Check.NotNull(tokenEstimator);
        _logger = Check.NotNull(logger);
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

        // Before: 预留配额
        var estimatedTokens = _tokenEstimator.Estimate(inputText);
        var reserveResult = await _quotaService.ReserveQuotaAsync(userId.Value, estimatedTokens, cancellationToken);

        if (!reserveResult.Succeeded)
        {
            _logger.LogWarning("Quota reservation failed for user {UserId}: {Error}", userId, reserveResult.Message);
            return new AgentRunResult
            {
                Response = reserveResult.Message ?? "Quota exceeded",
                FinishReason = "quota_exceeded"
            };
        }

        var reservation = reserveResult.Data!;

        try
        {
            // 执行下游管道
            var result = await next(context, cancellationToken);

            // After: 结算实际用量
            var actualTokens = (result.Usage?.PromptTokens ?? 0) + (result.Usage?.CompletionTokens ?? 0);
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

        // Before: 预留配额
        var estimatedTokens = _tokenEstimator.Estimate(inputText);
        var reserveResult = await _quotaService.ReserveQuotaAsync(userId, estimatedTokens, cancellationToken);

        if (!reserveResult.Succeeded)
        {
            _logger.LogWarning("Quota reservation failed for user {UserId}: {Error}", userId, reserveResult.Message);
            yield return new AgentStreamChunk
            {
                Text = reserveResult.Message ?? "Quota exceeded",
                FinishReason = "quota_exceeded"
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
            if (completedNormally)
            {
                var actualTokens = (lastUsage?.PromptTokens ?? 0) + (lastUsage?.CompletionTokens ?? 0);
                await _quotaService.SettleQuotaAsync(userId, reservation, actualTokens, CancellationToken.None);
            }
            else
            {
                // 异常/取消时优先使用实际用量，若无则回退到预估值
                var tokensToSettle = lastUsage != null
                    ? (lastUsage.PromptTokens + lastUsage.CompletionTokens)
                    : estimatedTokens;
                await _quotaService.SettleQuotaAsync(userId, reservation, tokensToSettle, CancellationToken.None);
            }
        }
    }
}
