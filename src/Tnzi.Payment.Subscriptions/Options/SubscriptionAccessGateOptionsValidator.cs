namespace Tnzi.Payment.Subscriptions.Options;

/// <summary>
/// 访问门禁配置验证器。写错的放行路径与「已经放行了」在运行时长得一样，所以启动期就拦下。
/// </summary>
public class SubscriptionAccessGateOptionsValidator : OptionsValidatorBase<SubscriptionAccessGateOptions>
{
    /// <summary>缓存时长上限（秒）：再长，补缴之后恢复得太慢，而失效事件丢失时只能靠它。</summary>
    public const int MaxCacheSeconds = 3600;

    /// <inheritdoc />
    protected override void ValidateOptions(SubscriptionAccessGateOptions options, List<string> errors)
    {
        if (options.CacheSeconds < 0 || options.CacheSeconds > MaxCacheSeconds)
            errors.Add($"AccessGate:CacheSeconds must be between 0 and {MaxCacheSeconds}.");

        foreach (var path in options.AllowedPaths ?? [])
        {
            if (string.IsNullOrWhiteSpace(path) || path.Trim().Trim('/').Length == 0)
                errors.Add("AccessGate:AllowedPaths cannot contain an empty path (it would allow every request).");
            else if (path.Contains('?') || path.Contains('*'))
                errors.Add($"AccessGate:AllowedPaths entry '{path}' must be a plain path prefix (no query string or wildcard).");
        }

        foreach (var code in options.BypassPermissions ?? [])
        {
            if (string.IsNullOrWhiteSpace(code))
                errors.Add("AccessGate:BypassPermissions cannot contain an empty permission code.");
        }
    }
}
