
namespace Tnzi.DataAnnotations;

/// <summary>
/// 密码验证特性
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public class PasswordAttribute : DataTypeAttribute
{
    private string? _value;

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
    /// 检查数据字段的值是否有效
    /// </summary>
    public override bool IsValid(object? value)
    {
        if (value == null)
        {
            return true;
        }

        var input = value as string;
        if (input == null)
        {
            return false;
        }

        _value = input;

        if (input.Length < RequiredLength)
        {
            return false;
        }

        if (RequiredDigit && !input.IsMatch(@"[0-9]"))
        {
            return false;
        }

        if (!CanOnlyDigit && input.IsMatch(@"^[0-9]+$"))
        {
            return false;
        }

        if (RequiredLowercase && !input.IsMatch(@"[a-z]"))
        {
            return false;
        }

        if (RequiredUppercase && !input.IsMatch(@"[A-Z]"))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 基于发生错误的数据字段对错误消息应用格式设置
    /// </summary>
    public override string FormatErrorMessage(string name)
    {
        if (string.IsNullOrEmpty(_value))
        {
            return base.FormatErrorMessage(name);
        }

        if (_value.Length < RequiredLength)
        {
            return $"{name} must be at least {RequiredLength} characters long.";
        }

        if (RequiredDigit && !_value.IsMatch(@"[0-9]"))
        {
            return $"{name} must contain at least one digit.";
        }

        if (!CanOnlyDigit && _value.IsMatch(@"^[0-9]+$"))
        {
            return $"{name} cannot consist solely of digits.";
        }

        if (RequiredLowercase && !_value.IsMatch(@"[a-z]"))
        {
            return $"{name} must contain at least one lowercase letter.";
        }

        if (RequiredUppercase && !_value.IsMatch(@"[A-Z]"))
        {
            return $"{name} must contain at least one uppercase letter.";
        }

        return base.FormatErrorMessage(name);
    }
}