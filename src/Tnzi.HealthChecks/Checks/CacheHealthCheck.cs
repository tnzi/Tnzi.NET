
namespace Tnzi.HealthChecks.Checks;

/// <summary>
/// 缓存服务健康检查
/// </summary>
public class CacheHealthCheck : IHealthCheck
{
    private readonly ICache _cache;
    /// <summary>
    /// 测试键前缀；每次检查再拼一个 Guid。缓存在多实例间共享，/health 与 /health/ready 又各自跑全部检查，
    /// 固定键会让一次检查读到另一次写入的值、或被另一次删掉，健康状态在 Healthy 与 Degraded 之间抖动。
    /// 30 秒过期兜底清理中途失败留下的键。
    /// </summary>
    private const string TestKeyPrefix = "__health_check_cache_test__:";

    public CacheHealthCheck(ICache cache)
    {
        _cache = Check.NotNull(cache);
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
            await _cache.SetAsync(testKey, testValue, TimeSpan.FromSeconds(30), cancellationToken);

            // 测试读取
            var readValue = await _cache.GetAsync<string>(testKey, cancellationToken);
            
            if (readValue != testValue)
            {
                return HealthCheckResult.Degraded("Cache read/write mismatch");
            }

            // 测试删除
            await _cache.RemoveAsync(testKey, cancellationToken);
            var afterDelete = await _cache.ExistsAsync(testKey, cancellationToken);
            
            if (afterDelete)
            {
                return HealthCheckResult.Degraded("Cache delete failed");
            }

            return HealthCheckResult.Healthy("Cache is working properly");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cache check failed", ex);
        }
    }
}