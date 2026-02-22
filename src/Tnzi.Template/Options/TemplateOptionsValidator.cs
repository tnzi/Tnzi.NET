namespace Tnzi.Template.Options;

/// <summary>
/// 模板选项验证器
/// </summary>
public class TemplateOptionsValidator : OptionsValidatorBase<TemplateOptions>
{
    protected override void ValidateOptions(TemplateOptions options, List<string> errors)
    {
        // 验证模板根路径
        if (string.IsNullOrWhiteSpace(options.TemplateRootPath))
        {
            errors.Add("Template.TemplateRootPath cannot be empty.");
        }

        // 验证缓存过期时间
        if (options.CacheExpirationSeconds < 0)
        {
            errors.Add("Template.CacheExpirationSeconds must be a non-negative number.");
        }

        // 验证模板扩展名
        if (string.IsNullOrWhiteSpace(options.TemplateExtension))
        {
            errors.Add("Template.TemplateExtension cannot be empty.");
        }
        else if (!options.TemplateExtension.StartsWith("."))
        {
            errors.Add("Template.TemplateExtension must start with a dot (e.g., '.cshtml').");
        }
    }
}
