
namespace Tnzi.Swagger.Options;

/// <summary>
/// Swagger 配置选项验证器
/// </summary>
public class SwaggerOptionsValidator : OptionsValidatorBase<SwaggerOptions>
{
    /// <summary>
    /// 验证 Swagger 配置选项
    /// </summary>
    protected override void ValidateOptions(SwaggerOptions options, List<string> errors)
    {
        if (!options.Enabled)
        {
            // 如果未启用，不需要验证其他配置
            return;
        }

        // 验证路由前缀
        if (string.IsNullOrWhiteSpace(options.RoutePrefix))
        {
            errors.Add("Swagger.RoutePrefix cannot be empty when Enabled is true.");
        }

        // 验证默认文档
        if (options.DefaultDocument != null)
        {
            if (string.IsNullOrWhiteSpace(options.DefaultDocument.Name))
            {
                errors.Add("Swagger.DefaultDocument.Name cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(options.DefaultDocument.Title))
            {
                errors.Add("Swagger.DefaultDocument.Title cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(options.DefaultDocument.Version))
            {
                errors.Add("Swagger.DefaultDocument.Version cannot be empty.");
            }
        }
        else
        {
            errors.Add("Swagger.DefaultDocument cannot be null.");
        }

        // 验证 JWT 认证配置
        if (options.JwtAuth != null && options.JwtAuth.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.JwtAuth.SchemeName))
            {
                errors.Add("Swagger.JwtAuth.SchemeName cannot be empty when Enabled is true.");
            }
        }
        else if (options.JwtAuth == null)
        {
            errors.Add("Swagger.JwtAuth cannot be null.");
        }

        // 验证 Swagger UI 配置
        if (options.SwaggerUI == null)
        {
            errors.Add("Swagger.SwaggerUI cannot be null.");
        }
    }
}