
namespace Tnzi.DataAnnotations;

/// <summary>
/// 手机号验证特性
/// </summary>
public class PhoneAttribute : ValidationAttribute
{
    /// <summary>
    /// 是否必填
    /// </summary>
    public bool Required { get; set; } = true;

    /// <summary>
    /// 验证值是否有效
    /// </summary>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
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

        if (Required && value == null)
        {
            string errorMessage;
            if (localizer != null)
            {
                var localized = localizer["Validation.Phone.Required", validationContext.DisplayName];
                errorMessage = localized.ResourceNotFound
                    ? $"{validationContext.DisplayName} is required."
                    : localized.Value;
            }
            else
            {
                errorMessage = $"{validationContext.DisplayName} is required.";
            }
            return new ValidationResult(errorMessage);
        }

        if (value == null || string.IsNullOrEmpty(value.ToString()))
        {
            return ValidationResult.Success;
        }

        var phone = value.ToString()!;

        if (phone.IsPhoneNumber())
        {
            return ValidationResult.Success;
        }

        string formatErrorMessage;
        if (localizer != null)
        {
            var localized = localizer["Validation.Phone.Format", validationContext.DisplayName];
            formatErrorMessage = localized.ResourceNotFound
                ? $"{validationContext.DisplayName} must be a valid phone number."
                : localized.Value;
        }
        else
        {
            formatErrorMessage = $"{validationContext.DisplayName} must be a valid phone number.";
        }
        return new ValidationResult(formatErrorMessage);
    }
}