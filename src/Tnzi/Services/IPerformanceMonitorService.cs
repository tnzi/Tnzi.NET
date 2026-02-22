
namespace Tnzi.Services;

/// <summary>
/// 性能监控服务接口
/// 负责收集和提供系统性能指标
/// </summary>
public interface IPerformanceMonitorService
{
    /// <summary>
    /// 记录数据库查询
    /// </summary>
    /// <param name="commandText">SQL命令</param>
    /// <param name="elapsed">执行耗时</param>
    /// <param name="isSlow">是否慢查询</param>
    void RecordDbQuery(string commandText, TimeSpan elapsed, bool isSlow = false);

    /// <summary>
    /// 记录缓存命中
    /// </summary>
    /// <param name="cacheKey">缓存键</param>
    void RecordCacheHit(string cacheKey);

    /// <summary>
    /// 记录缓存未命中
    /// </summary>
    /// <param name="cacheKey">缓存键</param>
    void RecordCacheMiss(string cacheKey);

    /// <summary>
    /// 获取当前性能快照
    /// </summary>
    /// <returns>性能指标快照</returns>
    PerformanceSnapshot GetSnapshot();
}

/// <summary>
/// 性能指标快照
/// </summary>
public class PerformanceSnapshot
{
    /// <summary>
    /// 总数据库查询次数
    /// </summary>
    public long TotalDbQueries { get; set; }

    /// <summary>
    /// 慢查询次数
    /// </summary>
    public long SlowDbQueries { get; set; }

    /// <summary>
    /// 平均查询耗时（毫秒）
    /// </summary>
    public double AverageDbQueryDurationMs { get; set; }

    /// <summary>
    /// 总缓存命中次数
    /// </summary>
    public long TotalCacheHits { get; set; }

    /// <summary>
    /// 总缓存未命中次数
    /// </summary>
    public long TotalCacheMisses { get; set; }

    /// <summary>
    /// 缓存命中率 (0.0 - 1.0)
    /// </summary>
    public double CacheHitRate => (TotalCacheHits + TotalCacheMisses) > 0 
        ? (double)TotalCacheHits / (TotalCacheHits + TotalCacheMisses) 
        : 0;

    /// <summary>
    /// 快照生成时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}