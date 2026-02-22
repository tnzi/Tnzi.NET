
namespace Tnzi.EFCore;

/// <summary>
/// 查询性能监控器
/// 用于记录慢查询和统计查询执行时间
/// </summary>
public class QueryPerformanceMonitor : IDisposable
{
    private readonly ILogger<QueryPerformanceMonitor>? _logger;
    private readonly TimeSpan _slowQueryThreshold;
    private readonly int _maxSlowQueries;
    private readonly Queue<SlowQueryRecord> _slowQueries;
    private readonly object _lock = new();

    /// <summary>
    /// 初始化查询性能监控器
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="slowQueryThreshold">慢查询阈值（默认1秒）</param>
    /// <param name="maxSlowQueries">最大慢查询记录数（默认100）</param>
    public QueryPerformanceMonitor(
        ILogger<QueryPerformanceMonitor>? logger = null,
        TimeSpan? slowQueryThreshold = null,
        int maxSlowQueries = 100)
    {
        _logger = logger;
        _slowQueryThreshold = slowQueryThreshold ?? TimeSpan.FromSeconds(1);
        _maxSlowQueries = maxSlowQueries;
        _slowQueries = new Queue<SlowQueryRecord>();
    }

    /// <summary>
    /// 记录查询执行
    /// </summary>
    /// <param name="sql">SQL语句</param>
    /// <param name="parameters">查询参数</param>
    /// <param name="executionTime">执行时间</param>
    /// <param name="rowCount">返回行数（如果可用）</param>
    public void RecordQuery(string sql, object? parameters, TimeSpan executionTime, int? rowCount = null)
    {
        if (executionTime > _slowQueryThreshold)
        {
            var record = new SlowQueryRecord
            {
                Sql = sql,
                Parameters = parameters,
                ExecutionTime = executionTime,
                RowCount = rowCount,
                Timestamp = DateTime.UtcNow
            };

            lock (_lock)
            {
                _slowQueries.Enqueue(record);
                if (_slowQueries.Count > _maxSlowQueries)
                {
                    _slowQueries.Dequeue();
                }
            }

            _logger?.LogWarning(
                "Slow query detected - ExecutionTime: {ExecutionTime}ms, SQL: {Sql}",
                executionTime.TotalMilliseconds,
                TruncateSql(sql, 200));
        }
    }

    /// <summary>
    /// 获取慢查询记录
    /// </summary>
    /// <param name="count">记录数量</param>
    /// <returns>慢查询记录列表（按时间倒序）</returns>
    public List<SlowQueryRecord> GetSlowQueries(int count = 10)
    {
        lock (_lock)
        {
            return _slowQueries
                .OrderByDescending(r => r.Timestamp)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// 获取指定时间窗口内的慢查询统计
    /// </summary>
    /// <param name="timeWindow">时间窗口</param>
    /// <returns>慢查询统计</returns>
    public SlowQueryStatistics GetStatistics(TimeSpan timeWindow)
    {
        var cutoff = DateTime.UtcNow.Subtract(timeWindow);
        
        lock (_lock)
        {
            var relevantQueries = _slowQueries
                .Where(r => r.Timestamp >= cutoff)
                .ToList();

            if (relevantQueries.Count == 0)
            {
                return new SlowQueryStatistics(0, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
            }

            var totalTime = TimeSpan.FromMilliseconds(relevantQueries.Sum(r => r.ExecutionTime.TotalMilliseconds));
            var avgTime = TimeSpan.FromMilliseconds(totalTime.TotalMilliseconds / relevantQueries.Count);
            var maxTime = relevantQueries.Max(r => r.ExecutionTime);
            var minTime = relevantQueries.Min(r => r.ExecutionTime);

            return new SlowQueryStatistics(relevantQueries.Count, avgTime, maxTime, minTime);
        }
    }

    /// <summary>
    /// 清空所有记录
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _slowQueries.Clear();
        }
    }

    /// <summary>
    /// 截断SQL语句（用于日志）
    /// </summary>
    private static string TruncateSql(string sql, int maxLength)
    {
        if (sql.Length <= maxLength)
            return sql;

        return string.Concat(sql.AsSpan(0, maxLength), "...");
    }

    public void Dispose()
    {
        Clear();
    }
}

/// <summary>
/// 慢查询记录
/// </summary>
public class SlowQueryRecord
{
    /// <summary>
    /// SQL语句
    /// </summary>
    public string Sql { get; set; } = string.Empty;

    /// <summary>
    /// 查询参数
    /// </summary>
    public object? Parameters { get; set; }

    /// <summary>
    /// 执行时间
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }

    /// <summary>
    /// 返回行数
    /// </summary>
    public int? RowCount { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 慢查询统计
/// </summary>
public record SlowQueryStatistics(
    int TotalCount,
    TimeSpan AverageExecutionTime,
    TimeSpan MaxExecutionTime,
    TimeSpan MinExecutionTime);