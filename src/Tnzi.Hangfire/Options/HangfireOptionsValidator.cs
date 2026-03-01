namespace Tnzi.Hangfire.Options;

/// <summary>
/// Hangfire 配置选项验证器
/// </summary>
public class HangfireOptionsValidator : OptionsValidatorBase<HangfireOptions>
{
    /// <summary>
    /// 验证 Hangfire 配置选项
    /// </summary>
    protected override void ValidateOptions(HangfireOptions options, List<string> errors)
    {
        // 验证存储类型和连接字符串
        if (options.StorageType != StorageType.Memory)
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                errors.Add($"Hangfire.ConnectionString is required when StorageType is {options.StorageType}.");
            }
        }

        // 验证 Dashboard 配置
        if (options.Dashboard != null)
        {
            if (options.Dashboard.Enabled && string.IsNullOrWhiteSpace(options.Dashboard.Path))
            {
                errors.Add("Hangfire.Dashboard.Path cannot be empty when Dashboard is enabled.");
            }

            if (!string.IsNullOrWhiteSpace(options.Dashboard.Path) && !options.Dashboard.Path.StartsWith("/", StringComparison.Ordinal))
            {
                errors.Add("Hangfire.Dashboard.Path must start with '/'.");
            }

            if (options.Dashboard.Enabled && !options.Dashboard.EnableAuthorization)
            {
                // Dashboard 开启但未启用授权时，任何人都可访问 /hangfire 查看和操作后台任务
                // 这是一个安全风险，在生产环境中强烈建议启用授权
                errors.Add("Hangfire.Dashboard.EnableAuthorization should be true when Dashboard is enabled. "
                    + "Set EnableAuthorization=true and configure AllowedRoles to restrict Dashboard access.");
            }

            if (options.Dashboard.EnableAuthorization &&
                (options.Dashboard.AllowedRoles == null || options.Dashboard.AllowedRoles.Count == 0))
            {
                errors.Add("Hangfire.Dashboard.AllowedRoles cannot be empty when EnableAuthorization is true.");
            }
        }
        else
        {
            errors.Add("Hangfire.Dashboard cannot be null.");
        }

        // 验证服务器选项
        if (options.Server != null)
        {
            if (options.Server.WorkerCount <= 0)
            {
                errors.Add("Hangfire.Server.WorkerCount must be greater than 0.");
            }

            if (options.Server.Queues == null || options.Server.Queues.Count == 0)
            {
                errors.Add("Hangfire.Server.Queues cannot be empty.");
            }
        }
        else
        {
            errors.Add("Hangfire.Server cannot be null.");
        }
    }
}
