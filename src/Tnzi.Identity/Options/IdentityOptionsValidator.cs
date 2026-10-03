
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

        // 宽限窗是一个安全折中：窗口有多长，一枚被盗令牌就有多长时间可以跟着用而不触发重放检测。
        // 上限钉在 5 分钟，是为了让「把它调大以绕开误报」这条路走不远 ——
        // 误报的正解是查为什么会并发刷新，不是把检测窗口拉到无意义。
        if (jwt.RefreshTokenRotationOverlapSeconds < 0)
            errors.Add("Jwt.RefreshTokenRotationOverlapSeconds cannot be negative (0 disables the overlap window).");
        else if (jwt.RefreshTokenRotationOverlapSeconds > 300)
            errors.Add("Jwt.RefreshTokenRotationOverlapSeconds must not exceed 300 (a longer window is a window in which a stolen refresh token goes undetected).");

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

        if (otp.RetentionHours < 0)
            errors.Add("Otp.RetentionHours cannot be negative (0 keeps expired codes forever).");

        // 验证 passkey 配置：两个枚举字符串直接喂给 WebAuthn 选项，拼错的值浏览器会拒绝整份选项，
        // 表现是「系统弹窗根本不出来」，在启动时说出来比让用户去猜强。
        var passkey = options.Passkey;
        if (passkey.ResidentKey is not null and not ("discouraged" or "preferred" or "required"))
            errors.Add("Passkey.ResidentKey must be one of: discouraged, preferred, required.");

        if (passkey.AuthenticatorAttachment is not null and not ("platform" or "cross-platform"))
            errors.Add("Passkey.AuthenticatorAttachment must be one of: platform, cross-platform.");

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

        // 提供商配置已迁到 AspNetCore:Captcha。旧键在这里从未被读过，静默接受等于让部署方以为自己配好了 reCAPTCHA
        // 而实际出的还是文字图；只能启动即失败并指路。
        if (!string.IsNullOrWhiteSpace(captcha.Provider))
            errors.Add("Captcha.Provider has moved: configure the captcha provider under AspNetCore:Captcha:Provider (recaptcha / recaptcha-v3 / hcaptcha / turnstile / altcha / image) and remove Identity:Captcha:Provider.");
        if (!string.IsNullOrWhiteSpace(captcha.SiteKey))
            errors.Add("Captcha.SiteKey has moved to AspNetCore:Captcha:SiteKey; remove Identity:Captcha:SiteKey.");
        if (!string.IsNullOrWhiteSpace(captcha.SecretKey))
            errors.Add("Captcha.SecretKey has moved to AspNetCore:Captcha:SecretKey; remove Identity:Captcha:SecretKey.");

        // 验证密码找回配置
        var recovery = options.Recovery;
        if (recovery.ResetTokenExpirationMinutes <= 0)
            errors.Add("Recovery.ResetTokenExpirationMinutes must be greater than 0.");

        // 验证第三方登录配置
        if (options.OAuth.LinkTokenLifetimeMinutes <= 0)
            errors.Add("OAuth.LinkTokenLifetimeMinutes must be greater than 0.");
    }
}