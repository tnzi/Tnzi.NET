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
/// Per-endpoint performance statistics
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
