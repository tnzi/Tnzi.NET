namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 限流算法类型
/// </summary>
public enum RateLimitAlgorithm
{
    /// <summary>
    /// 固定窗口算法（Fixed Window）
    /// 在固定时间窗口内计算请求数，窗口边界处可能出现流量突增
    /// </summary>
    FixedWindow = 0,

    /// <summary>
    /// 滑动窗口算法（Sliding Window）
    /// 使用滑动时间窗口计算请求数，更加平滑，但需要更多存储空间
    /// </summary>
    SlidingWindow = 1,

    /// <summary>
    /// 令牌桶算法（Token Bucket）
    /// 以固定速率生成令牌，请求消耗令牌，允许突发流量
    /// 注意：此算法需要更复杂的状态管理，当前版本暂未实现
    /// </summary>
    TokenBucket = 2,

    /// <summary>
    /// 漏桶算法（Leaky Bucket）
    /// 以固定速率处理请求，超出容量的请求被丢弃
    /// 注意：此算法需要更复杂的状态管理，当前版本暂未实现
    /// </summary>
    LeakyBucket = 3
}
