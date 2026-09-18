namespace Tnzi.DataAnnotations;

/// <summary>
/// 密码验证特性
/// </summary>
/// <remarks>
/// MVC 按 (类型, 属性) 缓存 <see cref="ValidationAttribute"/> 实例，一个 <c>[Password]</c> 属性对应唯一实例、
/// 全部并发请求共享，所以本特性必须是无状态的：哪条规则失败在
/// <see cref="IsValid(object?, ValidationContext)"/> 一次调用内判定并直接产出 <see cref="ValidationResult"/>，
/// 绝不把被校验的值存进实例字段（那会让并发请求拿到描述别人口令的错误信息，且明文口令常驻进程）。
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public class PasswordAttribute : DataTypeAttribute
{
    /// <summary>
    /// 初始化一个<see cref="PasswordAttribute"/>类型的新实例
    /// 默认：最小长度6、需要数字、不允许纯数字、需要小写字母、不需要大写字母
    /// </summary>
    public PasswordAttribute()
        : base(DataType.Password)
    {
        RequiredLength = 6;
        RequiredDigit = true;
        CanOnlyDigit = false;
        RequiredLowercase = true;
        RequiredUppercase = false;
    }

    /// <summary>
    /// 获取或设置 密码最小长度
    /// </summary>
    public int RequiredLength { get; set; }

    /// <summary>
    /// 获取或设置 需要数字
    /// </summary>
    public bool RequiredDigit { get; set; }

    /// <summary>
    /// 获取或设置 是否允许纯数字
    /// </summary>
    public bool CanOnlyDigit { get; set; }

    /// <summary>
    /// 获取或设置 需要小写字母
    /// </summary>
    public bool RequiredLowercase { get; set; }

    /// <summary>
    /// 获取或设置 需要大写字母
    /// </summary>
    public bool RequiredUppercase { get; set; }

    /// <summary>
    /// 检查数据字段的值是否有效（null 视为有效，交给 <c>[Required]</c> 决定）
    /// </summary>
    public override bool IsValid(object? value)
    {
        if (value == null)
        {
            return true;
        }

        return value is string input && FindViolation(input) == null;
    }

    /// <summary>
    /// 校验并在同一次调用内产出说明失败规则的 <see cref="ValidationResult"/>
    /// </summary>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value == null)
        {
            return ValidationResult.Success;
        }

        var displayName = validationContext.DisplayName;
        if (value is not string input)
        {
            return new ValidationResult(base.FormatErrorMessage(displayName), MemberNames(validationContext));
        }

        var violation = FindViolation(input);
        if (violation == null)
        {
            return ValidationResult.Success;
        }

        var (resourceKey, fallback) = violation.Value;
        var localized = ResolveLocalizer(validationContext)?[resourceKey, displayName, RequiredLength];
        var message = localized == null || localized.ResourceNotFound
            ? $"{displayName} {fallback}"
            : localized.Value;
        return new ValidationResult(message, MemberNames(validationContext));
    }

    /// <summary>
    /// 与具体值无关的规则描述：列出全部要求。按值报具体哪条规则失败的消息由
    /// <see cref="IsValid(object?, ValidationContext)"/> 产出。
    /// </summary>
    public override string FormatErrorMessage(string name)
    {
        var requirements = new List<string> { $"be at least {RequiredLength} characters long" };
        if (RequiredDigit)
            requirements.Add("contain at least one digit");
        if (!CanOnlyDigit)
            requirements.Add("not consist solely of digits");
        if (RequiredLowercase)
            requirements.Add("contain at least one lowercase letter");
        if (RequiredUppercase)
            requirements.Add("contain at least one uppercase letter");

        return $"{name} must {string.Join(", ", requirements)}.";
    }

    /// <summary>
    /// 按规则顺序找出第一条被违反的规则；返回 null 表示全部通过
    /// </summary>
    private (string ResourceKey, string Fallback)? FindViolation(string input)
    {
        if (input.Length < RequiredLength)
            return ("Validation.Password.Length", $"must be at least {RequiredLength} characters long.");

        if (RequiredDigit && !input.Any(char.IsAsciiDigit))
            return ("Validation.Password.Digit", "must contain at least one digit.");

        if (!CanOnlyDigit && input.All(char.IsAsciiDigit))
            return ("Validation.Password.OnlyDigits", "cannot consist solely of digits.");

        if (RequiredLowercase && !input.Any(char.IsAsciiLetterLower))
            return ("Validation.Password.Lowercase", "must contain at least one lowercase letter.");

        if (RequiredUppercase && !input.Any(char.IsAsciiLetterUpper))
            return ("Validation.Password.Uppercase", "must contain at least one uppercase letter.");

        return null;
    }

    private static IEnumerable<string>? MemberNames(ValidationContext validationContext)
    {
        return validationContext.MemberName == null ? null : [validationContext.MemberName];
    }

    /// <summary>
    /// 尝试获取本地化服务（如果启用）。Tnzi 不能依赖 Tnzi.Localization，故按名解析 SharedResource 类型，
    /// 与同目录 <see cref="PhoneAttribute"/> / <see cref="UsernameAttribute"/> 同一做法。
    /// </summary>
    private static IStringLocalizer? ResolveLocalizer(ValidationContext validationContext)
    {
        var factory = validationContext.GetService<IServiceProvider>()?.GetService<IStringLocalizerFactory>();
        if (factory == null)
            return null;

        var sharedResourceType = Type.GetType("Tnzi.Localization.Resources.SharedResource, Tnzi.Localization");
        return sharedResourceType == null ? null : factory.Create(sharedResourceType);
    }
}
