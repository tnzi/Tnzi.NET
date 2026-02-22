
namespace Tnzi.HealthChecks.Checks;

/// <summary>
/// 缓存服务健康检查
/// </summary>
public class CacheHealthCheck : IHealthCheck
{
    private readonly ICache _cache;
    private const string TestKey = "__health_check_cache_test__";

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
            // 测试写入
            var testValue = Guid.NewGuid().ToString();
            await _cache.SetAsync(TestKey, testValue, TimeSpan.FromSeconds(30), cancellationToken);

            // 测试读取
            var readValue = await _cache.GetAsync<string>(TestKey, cancellationToken);
            
            if (readValue != testValue)
            {
                return HealthCheckResult.Degraded("Cache read/write mismatch");
            }

            // 测试删除
            await _cache.RemoveAsync(TestKey, cancellationToken);
            var afterDelete = await _cache.ExistsAsync(TestKey, cancellationToken);
            
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