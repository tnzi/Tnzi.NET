
namespace Tnzi.Identity.Options;

/// <summary>
/// 会话配置验证器
/// </summary>
public class SessionOptionsValidator : OptionsValidatorBase<SessionOptions>
{
    protected override void ValidateOptions(SessionOptions options, List<string> errors)
    {
        // 验证过期时间
        if (options.ExpirationMinutes < 0)
        {
            errors.Add("Session.ExpirationMinutes cannot be negative.");
        }

        // 验证 Redis 键前缀
        if (options.StorageType == SessionStorageType.Redis)
        {
            if (string.IsNullOrWhiteSpace(options.RedisKeyPrefix))
            {
                errors.Add("Session.RedisKeyPrefix is required when StorageType is Redis.");
            }
        }
    }
}