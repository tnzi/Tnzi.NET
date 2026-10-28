namespace Tnzi.Performance.Models;

/// <summary>
/// 性能指标记录
/// </summary>
/// <param name="Path">原始请求路径（含路由参数的实际值），供慢请求明细使用</param>
/// <param name="Method">HTTP 方法</param>
/// <param name="StatusCode">客户端收到的状态码（下游抛异常时按异常折算）</param>
/// <param name="DurationMs">耗时（毫秒）</param>
/// <param name="RequestSizeBytes">请求体大小</param>
/// <param name="ResponseSizeBytes">响应体大小</param>
/// <param name="UserId">当前用户</param>
/// <param name="RequestId">请求 ID</param>
/// <param name="Timestamp">记录时间（UTC）</param>
/// <param name="Route">
/// 匹配端点的路由模板（如 <c>/api/users/{id}</c>），端点统计按它分组；没有匹配端点时为 <c>(unmatched)</c>。
/// 为 null 时（直接调用收集器、未经中间件的记录）统计退回按 <paramref name="Path"/> 分组。
/// </param>
public record PerformanceMetrics(
    string Path,
    string Method,
    int StatusCode,
    double DurationMs,
    long? RequestSizeBytes,
    long? ResponseSizeBytes,
    string? UserId,
    string? RequestId,
    DateTime Timestamp,
    string? Route = null)
{
    /// <summary>端点统计的分组键：路由模板，没有时退回原始路径。</summary>
    public string EndpointKey => Route ?? Path;
}

/// <summary>
/// 性能统计信息
/// </summary>
public record PerformanceStats(
    int TotalRequests,
    double AverageDurationMs,
    double MinDurationMs,
    double MaxDurationMs,
    int SlowRequestCount,
    Dictionary<string, int> ByPath,
    Dictionary<string, int> ByMethod,
    Dictionary<int, int> ByStatusCode);

/// <summary>
/// Percentile calculation result for request durations
/// </summary>
public record PercentileResult(
    double P50,
    double P95,
    double P99,
    double Average,
    double Min,
    double Max,
    int SampleCount);

/// <summary>
/// Per-endpoint performance statistics. <c>Path</c> is the route template of the endpoint
/// (for example <c>/api/users/{id}</c>), or <c>(unmatched)</c> for requests no endpoint matched.
/// </summary>
public record EndpointStats(
    string Path,
    string Method,
    int RequestCount,
    double AverageDurationMs,
    double P95DurationMs,
    double MinDurationMs,
    double MaxDurationMs,
    int ErrorCount,
    DateTime LastRequestTime);

/// <summary>
/// Slow request record with details
/// </summary>
public record SlowRequestRecord(
    string Path,
    string Method,
    int StatusCode,
    double DurationMs,
    string? UserId,
    string? RequestId,
    DateTime Timestamp);
