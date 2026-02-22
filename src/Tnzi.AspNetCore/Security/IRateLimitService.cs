
namespace Tnzi.AspNetCore.Security;

/// <summary>
/// 限流服务接口
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// 递增并获取当前计数（原子操作）
    /// 用于限流检查，先递增后检查，避免竞态条件
    /// </summary>
    /// <param name="key">限流键（IP、用户ID、路径等）</param>
    /// <param name="windowSeconds">时间窗口（秒）</param>
    /// <param name="algorithm">限流算法类型</param>
    /// <returns>递增后的计数值</returns>
    Task<long> IncrementAndGetAsync(string key, int windowSeconds, RateLimitAlgorithm algorithm = RateLimitAlgorithm.FixedWindow);
}