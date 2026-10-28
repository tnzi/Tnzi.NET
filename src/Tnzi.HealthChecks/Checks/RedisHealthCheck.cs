
namespace Tnzi.HealthChecks.Checks;

/// <summary>
/// Redis 健康检查
/// 用于检测 Redis 分布式缓存的连接状态
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly IDistributedCache _distributedCache;
    /// <summary>
    /// 测试键前缀；每次检查再拼一个 Guid。缓存在多实例间共享，/health 与 /health/ready 又各自跑全部检查，
    /// 固定键会让一次检查读到另一次写入的值、或被另一次删掉，健康状态在 Healthy 与 Degraded 之间抖动。
    /// 30 秒过期兜底清理中途失败留下的键。
    /// </summary>
    private const string TestKeyPrefix = "__health_check_redis_test__:";

    public RedisHealthCheck(IDistributedCache distributedCache)
    {
        _distributedCache = Check.NotNull(distributedCache);
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 测试写入（每次检查一个独立的键）
            var testValue = Guid.NewGuid().ToString();
            var testKey = TestKeyPrefix + testValue;
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            };
            
            await _distributedCache.SetStringAsync(testKey, testValue, options, cancellationToken);

            // 测试读取
            var readValue = await _distributedCache.GetStringAsync(testKey, cancellationToken);
            
            if (readValue != testValue)
            {
                return HealthCheckResult.Degraded("Redis read/write mismatch");
            }

            // 测试删除
            await _distributedCache.RemoveAsync(testKey, cancellationToken);
            var afterDelete = await _distributedCache.GetStringAsync(testKey, cancellationToken);
            
            if (afterDelete != null)
            {
                return HealthCheckResult.Degraded("Redis delete failed");
            }

            return HealthCheckResult.Healthy("Redis is working properly");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Redis check failed. Make sure Redis is running and connection string is correct.",
                ex);
        }
    }
}