
namespace Tnzi.DataAnnotations;

/// <summary>
/// 用户名验证特性
/// </summary>
public class UsernameAttribute : ValidationAttribute
{
    /// <summary>
    /// 最小长度
    /// </summary>
    public int MinLength { get; set; } = 3;

    /// <summary>
    /// 最大长度
    /// </summary>
    public int MaxLength { get; set; } = 20;

    /// <summary>
    /// 验证值是否有效
    /// </summary>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value == null || string.IsNullOrEmpty(value.ToString()))
        {
            return ValidationResult.Success;
        }

        var username = value.ToString()!;

        // 尝试获取本地化服务（如果启用）
        // 注意：由于 Tnzi 不能依赖 Tnzi.Localization，使用反射获取 SharedResource 类型
        IStringLocalizer? localizer = null;
        var serviceProvider = validationContext.GetService<IServiceProvider>();
        if (serviceProvider != null)
        {
            var factory = serviceProvider.GetService<IStringLocalizerFactory>();
            if (factory != null)
            {
                // 使用反射获取 SharedResource 类型（在 Tnzi.Localization 中）
                var sharedResourceType = Type.GetType("Tnzi.Localization.Resources.SharedResource, Tnzi.Localization");
                if (sharedResourceType != null)
                {
                    localizer = factory.Create(sharedResourceType);
                }
            }
        }

        if (username.Length < MinLength || username.Length > MaxLength)
        {
            string errorMessage;
            if (localizer != null)
            {
                var localized = localizer["Validation.Username.Length", validationContext.DisplayName, MinLength, MaxLength];
                errorMessage = localized.ResourceNotFound
                    ? $"{validationContext.DisplayName} must be between {MinLength} and {MaxLength} characters long."
                    : localized.Value;
            }
            else
            {
                errorMessage = $"{validationContext.DisplayName} must be between {MinLength} and {MaxLength} characters long.";
            }
            return new ValidationResult(errorMessage);
        }

        const string pattern = @"^[a-zA-Z0-9._-]+$";
        if (Regex.IsMatch(username, pattern))
        {
            return ValidationResult.Success;
        }

        string formatErrorMessage;
        if (localizer != null)
        {
            var localized = localizer["Validation.Username.Format", validationContext.DisplayName];
            formatErrorMessage = localized.ResourceNotFound
                ? $"{validationContext.DisplayName} can only contain letters, numbers, dots, underscores, and hyphens."
                : localized.Value;
        }
        else
        {
            formatErrorMessage = $"{validationContext.DisplayName} can only contain letters, numbers, dots, underscores, and hyphens.";
        }
        return new ValidationResult(formatErrorMessage);
    }
}