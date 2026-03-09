namespace Tnzi.AI.Services.Interfaces;

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
}
