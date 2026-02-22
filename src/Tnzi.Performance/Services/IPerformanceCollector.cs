
namespace Tnzi.Performance.Services;

/// <summary>
/// 性能收集器接口
/// </summary>
public interface IPerformanceCollector
{
    /// <summary>
    /// 记录性能指标
    /// </summary>
    /// <param name="metrics">性能指标</param>
    void Record(PerformanceMetrics metrics);

    /// <summary>
    /// 获取指定时间窗口内的性能统计
    /// </summary>
    /// <param name="timeWindow">时间窗口</param>
    /// <param name="slowRequestThresholdMs">慢请求阈值（毫秒）</param>
    /// <returns>性能统计信息</returns>
    PerformanceStats GetStats(TimeSpan timeWindow, double? slowRequestThresholdMs = null);

    /// <summary>
    /// 获取最近的性能记录
    /// </summary>
    /// <param name="count">记录数量</param>
    /// <returns>最近的性能记录</returns>
    List<PerformanceMetrics> GetRecentRecords(int count);

    /// <summary>
    /// 获取慢请求记录
    /// </summary>
    /// <param name="thresholdMs">阈值（毫秒）</param>
    /// <param name="count">记录数量</param>
    /// <returns>慢请求记录</returns>
    List<PerformanceMetrics> GetSlowRequests(double thresholdMs, int count);

    /// <summary>
    /// 清空所有统计记录
    /// </summary>
    void Clear();
}
