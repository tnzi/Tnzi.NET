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

    /// <summary>
    /// 记录使用日志：调用方给出成本（null = 按配置的单价算），同时记下缓存指标。
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
        int cachedInputTokens,
        int cacheCreationTokens,
        CancellationToken ct = default)
    {
        // 默认实现：忽略缓存指标（向后兼容）
        return LogUsageAsync(operationType, provider, model, inputTokens, outputTokens, durationMs, isSuccess, errorMessage, agentId, threadId, estimatedCostUsd, ct);
    }

    /// <summary>
    /// 记录使用日志：一行的全部字段，含这一行计在谁名下（<see cref="UsageLogEntry.UserId"/>）。
    /// </summary>
    /// <remarks>
    /// 其余重载都不带用户，由服务按当前运行请求、当前登录用户、线程创建人推断。调用方知道用户、
    /// 却不在该用户的登录上下文里（后台任务、按运行令牌认证的模型网关）时用这个重载显式给出。
    /// 默认实现转给不带用户的重载（自己实现本接口的旧实现照常工作，只是记不下显式给出的用户）。
    /// </remarks>
    Task LogUsageAsync(UsageLogEntry entry, CancellationToken ct = default)
    {
        Check.NotNull(entry);
        return LogUsageAsync(entry.OperationType, entry.Provider, entry.Model, entry.InputTokens, entry.OutputTokens,
            entry.DurationMs, entry.IsSuccess, entry.ErrorMessage, entry.AgentId, entry.ThreadId, entry.EstimatedCostUsd,
            entry.CachedInputTokens, entry.CacheCreationTokens, ct);
    }
}
