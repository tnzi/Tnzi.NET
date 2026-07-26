
namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 异常统计服务接口
/// </summary>
public interface IExceptionStatistics
{
    /// <summary>
    /// 记录异常
    /// </summary>
    /// <param name="exception">异常对象</param>
    /// <param name="requestId">请求 ID</param>
    void RecordException(Exception exception, string? requestId);

    /// <summary>
    /// 获取指定时间窗口内的异常统计
    /// </summary>
    /// <param name="timeWindow">时间窗口</param>
    /// <returns>异常统计信息</returns>
    ExceptionStats GetStats(TimeSpan timeWindow);

    /// <summary>
    /// 获取最近的异常记录
    /// </summary>
    /// <param name="count">记录数量</param>
    /// <returns>最近的异常记录</returns>
    List<ExceptionRecord> GetRecentExceptions(int count);

    /// <summary>
    /// 清空所有统计记录
    /// </summary>
    void Clear();
}

/// <summary>
/// 异常统计信息
/// </summary>
public record ExceptionStats(
    int TotalCount,
    Dictionary<string, int> ByType,
    Dictionary<int, int> ByStatusCode,
    Dictionary<string, int> ByErrorCode);

/// <summary>
/// 异常记录
/// </summary>
public record ExceptionRecord(
    string ExceptionType,
    string? Message,
    int? StatusCode,
    string? ErrorCode,
    string? RequestId,
    DateTime Timestamp);

/// <summary>
/// 异常统计服务实现
/// </summary>
public class ExceptionStatistics : IExceptionStatistics
{
    private readonly ConcurrentQueue<ExceptionRecord> _recentExceptions = new();
    private readonly int _maxHistorySize;

    public ExceptionStatistics(int maxHistorySize = 100)
    {
        _maxHistorySize = maxHistorySize;
    }

    public void RecordException(Exception exception, string? requestId)
    {
        var record = new ExceptionRecord(
            exception.GetType().Name,
            exception.Message,
            GetStatusCode(exception),
            GetErrorCode(exception),
            requestId,
            DateTime.UtcNow);

        _recentExceptions.Enqueue(record);

        // 限制历史记录大小（批量清理避免高并发下的性能问题）
        var excessCount = _recentExceptions.Count - _maxHistorySize;
        for (var i = 0; i < excessCount; i++)
        {
            _recentExceptions.TryDequeue(out _);
        }
    }

    public ExceptionStats GetStats(TimeSpan timeWindow)
    {
        var cutoff = DateTime.UtcNow.Subtract(timeWindow);
        var relevantRecords = _recentExceptions
            .Where(r => r.Timestamp >= cutoff)
            .ToList();

        return new ExceptionStats(
            TotalCount: relevantRecords.Count,
            ByType: relevantRecords
                .GroupBy(r => r.ExceptionType)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByStatusCode: relevantRecords
                .Where(r => r.StatusCode.HasValue)
                .GroupBy(r => r.StatusCode!.Value)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByErrorCode: relevantRecords
                .Where(r => !string.IsNullOrEmpty(r.ErrorCode))
                .GroupBy(r => r.ErrorCode!)
                .ToDictionary(g => g.Key, g => g.Count()));
    }

    public List<ExceptionRecord> GetRecentExceptions(int count)
    {
        // 返回最近的记录（最新的在前）
        return _recentExceptions
            .OrderByDescending(r => r.Timestamp)
            .Take(count)
            .ToList();
    }

    public void Clear()
    {
        while (_recentExceptions.TryDequeue(out _)) { }
    }

    private static int? GetStatusCode(Exception exception)
    {
        return exception switch
        {
            BusinessException bEx => bEx.HttpStatusCode,
            InfrastructureException => 503,
            _ => null
        };
    }

    private static string? GetErrorCode(Exception exception)
    {
        return exception switch
        {
            TnziException tzEx => tzEx.Code,
            _ => null
        };
    }
}