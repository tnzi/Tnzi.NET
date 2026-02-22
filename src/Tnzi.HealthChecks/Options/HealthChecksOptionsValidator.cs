
namespace Tnzi.HealthChecks.Options;

/// <summary>
/// 健康检查配置选项验证器
/// </summary>
public class HealthChecksOptionsValidator : OptionsValidatorBase<HealthChecksOptions>
{
    /// <summary>
    /// 验证健康检查配置选项
    /// </summary>
    protected override void ValidateOptions(HealthChecksOptions options, List<string> errors)
    {
        if (!options.Enabled)
        {
            // 如果未启用，不需要验证其他配置
            return;
        }

        // 验证主路径
        ValidatePath(options.Path, "HealthChecks.Path", errors);

        // 验证存活探针路径
        ValidatePath(options.LivenessPath, "HealthChecks.LivenessPath", errors);

        // 验证就绪探针路径
        ValidatePath(options.ReadinessPath, "HealthChecks.ReadinessPath", errors);

        // 验证超时时间
        if (options.TimeoutSeconds <= 0)
        {
            errors.Add("HealthChecks.TimeoutSeconds must be greater than 0.");
        }

        // 验证缓存时长
        if (options.CacheDurationSeconds < 0)
        {
            errors.Add("HealthChecks.CacheDurationSeconds must be greater than or equal to 0.");
        }
    }

    /// <summary>
    /// 验证路径格式
    /// </summary>
    private static void ValidatePath(string path, string propertyName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"{propertyName} cannot be empty when Enabled is true.");
        }
        else if (!path.StartsWith('/'))
        {
            errors.Add($"{propertyName} must start with '/'.");
        }
    }
}
