
namespace Tnzi.Services;

/// <summary>
/// 性能监控服务实现
/// 使用 System.Diagnostics.Metrics 和内部计数器收集指标
/// </summary>
public class PerformanceMonitorService : IPerformanceMonitorService, IDisposable
{
    // Metrics
    private readonly Meter _meter;
    private readonly Counter<long> _dbQueryCounter;
    private readonly Counter<long> _slowDbQueryCounter;
    private readonly Histogram<double> _dbQueryDuration;
    private readonly Counter<long> _cacheHitCounter;
    private readonly Counter<long> _cacheMissCounter;
    
    // Internal counters for snapshot
    private long _totalDbQueries;
    private long _slowDbQueries;
    private long _totalCacheHits;
    private long _totalCacheMisses;
    private long _totalDbQueryDurationTicks; // Use Ticks for atomic add

    /// <summary>
    /// 初始化性能监控服务
    /// </summary>
    public PerformanceMonitorService()
    {
        _meter = new Meter("Tnzi.Framework", "1.0.0");
        
        _dbQueryCounter = _meter.CreateCounter<long>("db_queries_total", description: "Total number of database queries executed");
        _slowDbQueryCounter = _meter.CreateCounter<long>("db_queries_slow_total", description: "Total number of slow database queries executed");
        _dbQueryDuration = _meter.CreateHistogram<double>("db_query_duration_ms", unit: "ms", description: "Database query duration");
        
        _cacheHitCounter = _meter.CreateCounter<long>("cache_hits_total", description: "Total number of cache hits");
        _cacheMissCounter = _meter.CreateCounter<long>("cache_misses_total", description: "Total number of cache misses");
    }

    /// <inheritdoc />
    public void RecordDbQuery(string commandText, TimeSpan elapsed, bool isSlow = false)
    {
        _dbQueryCounter.Add(1);
        _dbQueryDuration.Record(elapsed.TotalMilliseconds);
        
        Interlocked.Increment(ref _totalDbQueries);
        Interlocked.Add(ref _totalDbQueryDurationTicks, elapsed.Ticks);

        if (isSlow)
        {
            _slowDbQueryCounter.Add(1);
            Interlocked.Increment(ref _slowDbQueries);
        }
    }

    /// <inheritdoc />
    public void RecordCacheHit(string cacheKey)
    {
        _cacheHitCounter.Add(1, new KeyValuePair<string, object?>("key", cacheKey));
        Interlocked.Increment(ref _totalCacheHits);
    }

    /// <inheritdoc />
    public void RecordCacheMiss(string cacheKey)
    {
        _cacheMissCounter.Add(1, new KeyValuePair<string, object?>("key", cacheKey));
        Interlocked.Increment(ref _totalCacheMisses);
    }

    /// <inheritdoc />
    public PerformanceSnapshot GetSnapshot()
    {
        var totalQueries = Interlocked.Read(ref _totalDbQueries);
        var totalDurationTicks = Interlocked.Read(ref _totalDbQueryDurationTicks);
        
        return new PerformanceSnapshot
        {
            TotalDbQueries = totalQueries,
            SlowDbQueries = Interlocked.Read(ref _slowDbQueries),
            AverageDbQueryDurationMs = totalQueries > 0 
                ? TimeSpan.FromTicks((long)((double)totalDurationTicks / totalQueries)).TotalMilliseconds 
                : 0,
            TotalCacheHits = Interlocked.Read(ref _totalCacheHits),
            TotalCacheMisses = Interlocked.Read(ref _totalCacheMisses),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _meter.Dispose();
    }
}