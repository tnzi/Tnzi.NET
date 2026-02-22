
namespace Tnzi.HealthChecks.Checks;

/// <summary>
/// Redis 健康检查
/// 用于检测 Redis 分布式缓存的连接状态
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly IDistributedCache _distributedCache;
    private const string TestKey = "__health_check_redis_test__";

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
            // 测试写入
            var testValue = Guid.NewGuid().ToString();
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            };
            
            await _distributedCache.SetStringAsync(TestKey, testValue, options, cancellationToken);

            // 测试读取
            var readValue = await _distributedCache.GetStringAsync(TestKey, cancellationToken);
            
            if (readValue != testValue)
            {
                return HealthCheckResult.Degraded("Redis read/write mismatch");
            }

            // 测试删除
            await _distributedCache.RemoveAsync(TestKey, cancellationToken);
            var afterDelete = await _distributedCache.GetStringAsync(TestKey, cancellationToken);
            
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