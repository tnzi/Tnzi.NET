namespace Tnzi.Identity.Metadata;

/// <summary>
/// Identity模块常量定义
/// </summary>
public static class IdentityConstants
{
    /// <summary>
    /// Token提供者类型常量
    /// </summary>
    public static class TokenProvider
    {
        public const string JWT = "JWT";
        public const string TwoFactor = "2FA";
        public const string Identity = "Identity";
    }

    /// <summary>
    /// Token名称常量
    /// </summary>
    public static class TokenName
         
    {
        public const string RefreshToken = "RefreshToken";
        public const string TempToken = "TempToken";
        public const string SetPassword = "SetPassword";
    }

    /// <summary>
    /// 登录提供者常量
    /// </summary>
    public static class LoginProvider
    {
        public const string JWT = "JWT";
        public const string CodeLogin = "CodeLogin";
        public const string Registration = "Registration";
    }

    /// <summary>
    /// 2FA类型名称常量
    /// </summary>
    public static class TwoFactorTypeName
    {
        public const string Sms = "Sms";
        public const string Email = "Email";
        public const string Totp = "Totp";
    }

    /// <summary>
    /// UserDetail字段名常量
    /// </summary>
    public static class UserDetailField
    {
        public const string Nickname = "Nickname";
        public const string Avatar = "Avatar";
    }
}
