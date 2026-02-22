
namespace Tnzi.Caching;

/// <summary>
/// 缓存配置选项验证器
/// </summary>
public class CachingOptionsValidator : OptionsValidatorBase<CachingOptions>
{
    /// <summary>
    /// 验证缓存配置选项
    /// </summary>
    protected override void ValidateOptions(CachingOptions options, List<string> errors)
    {
        // 验证默认过期时间
        if (options.DefaultExpirationMinutes <= 0)
        {
            errors.Add("Caching.DefaultExpirationMinutes must be greater than 0.");
        }

        // 验证缓存类型
        if (!string.IsNullOrEmpty(options.Type))
        {
            var validTypes = new[] { "Memory", "Redis" };
            if (!validTypes.Contains(options.Type, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Caching.Type must be one of: {string.Join(", ", validTypes)}.");
            }
        }

        // 条件验证：当使用 Redis 时，验证 Redis 配置
        if (string.Equals(options.Type, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(options.RedisConnectionString))
            {
                errors.Add("Caching.RedisConnectionString is required when Type is Redis.");
            }
        }

        // 验证过期策略
        if (!string.IsNullOrEmpty(options.ExpirationStrategy))
        {
            var validStrategies = new[] { "Fixed", "Pattern", "Sliding", "Composite" };
            if (!validStrategies.Contains(options.ExpirationStrategy, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Caching.ExpirationStrategy must be one of: {string.Join(", ", validStrategies)}.");
            }
        }
    }
}