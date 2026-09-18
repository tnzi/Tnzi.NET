namespace Tnzi.System.Options;

/// <summary>
/// <see cref="AccessLogOptions"/> 配置验证器。
/// </summary>
public class AccessLogOptionsValidator : OptionsValidatorBase<AccessLogOptions>
{
    protected override void ValidateOptions(AccessLogOptions options, List<string> errors)
    {
        if (options.QueueCapacity <= 0)
        {
            errors.Add("QueueCapacity must be a positive number.");
        }

        if (options.ExcludedPaths == null)
        {
            errors.Add("ExcludedPaths must not be null (use an empty array to exclude nothing).");
            return;
        }

        foreach (var path in options.ExcludedPaths)
        {
            // 匹配走 PathString.StartsWithSegments：不以 / 开头的项永远匹配不上，
            // 症状是「配了排除却照常采集」而没有任何报错。
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
            {
                errors.Add($"ExcludedPaths entry '{path}' must be a path prefix starting with '/'.");
            }
        }
    }
}
