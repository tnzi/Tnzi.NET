namespace Tnzi.AI.Services;

/// <summary>
/// 使用日志服务接口
/// </summary>
public interface IUsageLogService
{
    /// <summary>
    /// 记录使用日志
    /// </summary>
    Task LogUsageAsync(
        string operationType,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        long durationMs,
        bool isSuccess,
        string? errorMessage = null,
        Guid? agentId = null,
        Guid? threadId = null,
        CancellationToken ct = default);

    /// <summary>
    /// 记录使用日志（含成本计算）
    /// </summary>
    Task LogUsageAsync(
        string operationType,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        long durationMs,
        bool isSuccess,
        string? errorMessage,
        Guid? agentId,
        Guid? threadId,
        decimal? estimatedCostUsd,
        CancellationToken ct = default)
    {
        // 默认实现：忽略 estimatedCostUsd，调用原方法（向后兼容）
        return LogUsageAsync(operationType, provider, model, inputTokens, outputTokens, durationMs, isSuccess, errorMessage, agentId, threadId, ct);
    }

    /// <summary>
    /// 记录使用日志（含缓存指标）
    /// </summary>
    Task LogUsageAsync(
        string operationType,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        long durationMs,
        bool isSuccess,
        string? errorMessage,
        Guid? agentId,
        Guid? threadId,
        int cachedInputTokens,
        int cacheCreationTokens,
        CancellationToken ct = default)
    {
        // 默认实现：忽略缓存指标（向后兼容）
        return LogUsageAsync(operationType, provider, model, inputTokens, outputTokens, durationMs, isSuccess, errorMessage, agentId, threadId, ct);
    }
}
