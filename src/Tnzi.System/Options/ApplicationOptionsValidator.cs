namespace Tnzi.System.Options;

/// <summary>
/// ApplicationOptions 配置验证器
/// </summary>
public class ApplicationOptionsValidator : OptionsValidatorBase<ApplicationOptions>
{
    protected override void ValidateOptions(ApplicationOptions options, List<string> errors)
    {
        // 验证必填字段
        if (string.IsNullOrWhiteSpace(options.AppName))
        {
            errors.Add("AppName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SiteName))
        {
            errors.Add("SiteName is required.");
        }

        // 验证URL格式（如果提供）
        if (!string.IsNullOrWhiteSpace(options.FrontendUrl) && !options.FrontendUrl.IsUrl())
        {
            errors.Add("FrontendUrl must be a valid URL.");
        }

        if (!string.IsNullOrWhiteSpace(options.ApiBaseUrl) && !options.ApiBaseUrl.IsUrl())
        {
            errors.Add("ApiBaseUrl must be a valid URL.");
        }

        if (!string.IsNullOrWhiteSpace(options.WebsiteUrl) && !options.WebsiteUrl.IsUrl())
        {
            errors.Add("WebsiteUrl must be a valid URL.");
        }

        if (!string.IsNullOrWhiteSpace(options.LogoUrl) && !options.LogoUrl.IsUrl())
        {
            errors.Add("LogoUrl must be a valid URL.");
        }

        // 验证邮箱格式（如果提供）
        if (!string.IsNullOrWhiteSpace(options.Email) && !options.Email.IsEmail())
        {
            errors.Add("Email must be a valid email address.");
        }
    }
}
