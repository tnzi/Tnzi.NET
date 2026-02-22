namespace Tnzi.Performance.Models;

/// <summary>
/// 性能指标记录
/// </summary>
public record PerformanceMetrics(
    string Path,
    string Method,
    int StatusCode,
    double DurationMs,
    long? RequestSizeBytes,
    long? ResponseSizeBytes,
    string? UserId,
    string? RequestId,
    DateTime Timestamp);

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
