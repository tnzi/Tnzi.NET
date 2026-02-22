
namespace Tnzi.EFCore.Options;

/// <summary>
/// EFCore 配置选项验证器
/// </summary>
public class EFCoreOptionsValidator : OptionsValidatorBase<EFCoreOptions>
{
    /// <summary>
    /// 验证 EFCore 配置选项
    /// </summary>
    protected override void ValidateOptions(EFCoreOptions options, List<string> errors)
    {
        // 验证慢查询阈值
        if (options.SlowQueryThresholdMs <= 0)
        {
            errors.Add("EFCore.SlowQueryThresholdMs must be greater than 0.");
        }

        // 验证批次大小
        if (options.BatchSize <= 0)
        {
            errors.Add("EFCore.BatchSize must be greater than 0.");
        }

        // 验证按实体类型配置的批次大小
        if (options.BatchSizes != null)
        {
            foreach (var kvp in options.BatchSizes)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    errors.Add("EFCore.BatchSizes contains an entry with empty or null key.");
                }
                else if (kvp.Value <= 0)
                {
                    errors.Add($"EFCore.BatchSizes[\"{kvp.Key}\"] must be greater than 0.");
                }
            }
        }
    }
}