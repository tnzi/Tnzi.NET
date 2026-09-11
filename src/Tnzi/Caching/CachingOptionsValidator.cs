
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

        // 刻意不在这里要求 RedisConnectionString：连接串有三个来源
        // （Redis.ConnectionString > Caching.RedisConnectionString > ConnectionStrings:Redis），
        // 本类只看得见第二个。此前在这里硬性要求它，让按文档只配 Redis.ConnectionString 的应用
        // 启动即 OptionsValidationException。三个来源都没配时由 RedisCachingModule 在解析处抛出，
        // 错误消息列出全部三个键；Type=Redis 却没加载 Redis 模块则由 CachingModule 的启动自证拦住。

    }
}