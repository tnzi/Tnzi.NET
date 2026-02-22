
namespace Tnzi.Identity.Options;

/// <summary>
/// IdentityOptions 配置验证器
/// </summary>
public class IdentityOptionsValidator : OptionsValidatorBase<IdentityOptions>
{
    protected override void ValidateOptions(IdentityOptions options, List<string> errors)
    {
        // 验证 JWT 配置
        var jwt = options.Jwt;
        if (!string.IsNullOrEmpty(jwt.SecretKey) && jwt.SecretKey.Length < 32)
            errors.Add("Jwt.SecretKey must be at least 32 characters long for security.");

        if (jwt.AccessTokenExpirationMinutes <= 0)
            errors.Add("Jwt.AccessTokenExpirationMinutes must be greater than 0.");

        if (jwt.RefreshTokenExpirationDays <= 0)
            errors.Add("Jwt.RefreshTokenExpirationDays must be greater than 0.");

        // 验证 OTP 配置
        var otp = options.Otp;
        if (otp.CodeLength < 4 || otp.CodeLength > 8)
            errors.Add("Otp.CodeLength must be between 4 and 8.");

        if (otp.ExpirationMinutes <= 0)
            errors.Add("Otp.ExpirationMinutes must be greater than 0.");

        if (otp.ResendIntervalSeconds < 0)
            errors.Add("Otp.ResendIntervalSeconds cannot be negative.");

        if (otp.MaxAttempts <= 0)
            errors.Add("Otp.MaxAttempts must be greater than 0.");

        // 验证密码策略
        var pwd = options.PasswordPolicy;
        if (pwd.MinLength < 4)
            errors.Add("PasswordPolicy.MinLength must be at least 4.");

        if (pwd.PasswordHistoryCount < 0)
            errors.Add("PasswordPolicy.PasswordHistoryCount cannot be negative.");

        if (pwd.PasswordExpirationDays < 0)
            errors.Add("PasswordPolicy.PasswordExpirationDays cannot be negative.");

        // 验证账户安全配置
        var security = options.AccountSecurity;
        if (security.MaxFailedLoginAttempts <= 0)
            errors.Add("AccountSecurity.MaxFailedLoginAttempts must be greater than 0.");

        if (security.LockoutDurationMinutes <= 0)
            errors.Add("AccountSecurity.LockoutDurationMinutes must be greater than 0.");

        if (security.SessionTimeoutMinutes < 0)
            errors.Add("AccountSecurity.SessionTimeoutMinutes cannot be negative.");

        // 验证多点登录配置
        var multiLogin = options.MultiLogin;
        if (multiLogin.MaxConcurrentSessions < 0)
            errors.Add("MultiLogin.MaxConcurrentSessions cannot be negative.");

        // 验证验证码配置
        var captcha = options.Captcha;
        if (captcha.CaptchaFailThreshold < 0)
            errors.Add("Captcha.CaptchaFailThreshold cannot be negative.");

        // 验证密码找回配置
        var recovery = options.Recovery;
        if (recovery.ResetTokenExpirationMinutes <= 0)
            errors.Add("Recovery.ResetTokenExpirationMinutes must be greater than 0.");
    }
}