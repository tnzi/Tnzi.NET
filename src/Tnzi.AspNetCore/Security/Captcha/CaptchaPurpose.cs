namespace Tnzi.AspNetCore.Security;

/// <summary>
/// 人机验证用途名的形态约束：小写字母开头，小写字母 / 数字 / 连字符，1 到 32 位。
/// </summary>
/// <remarks>
/// 用途会进缓存键、Altcha 的 salt 查询参数与提供商的 action 字段，所以只放一种保守形态。
/// 出题端点把它当外部输入校验（图形验证码的 <c>auth/captcha/{purpose}</c> 此前只认 login / register，
/// 放开成任意用途是为了让消费方自己的端点也能用内置图形验证码，但不能放开成任意字符串）。
/// </remarks>
public static class CaptchaPurpose
{
    private static readonly Regex Pattern = new("^[a-z][a-z0-9-]{0,31}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>登录。</summary>
    public const string Login = "login";

    /// <summary>注册。</summary>
    public const string Register = "register";

    /// <summary>找回密码。</summary>
    public const string PasswordRecovery = "password-recovery";

    /// <summary>是否是合法的用途名。</summary>
    public static bool IsValid(string? purpose) => purpose != null && Pattern.IsMatch(purpose);
}
