using TokenResult = Tnzi.Identity.Services.TokenResult;

namespace Tnzi.Identity.Controllers;

/// <summary>
/// 认证控制器
/// 提供认证相关的API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("auth")]
public class DefaultAuthController : ApiControllerBase
{
    protected readonly ITwoFactorService TwoFactorService;
    protected readonly IAuthService AuthService;
    protected readonly IRegistrationService RegistrationService;
    protected readonly IPasswordService PasswordService;
    protected readonly IOAuthService? OAuthService;
    protected readonly ICaptchaService? CaptchaService;
    protected readonly IOptionsMonitor<IdentityOptions>? IdentityOptions;
    protected readonly IConfiguration? Configuration;
    protected readonly IIdentityPageService? IdentityPageService;
    protected readonly IPasswordPolicyService? PasswordPolicyService;

    /// <summary>
    /// 初始化认证控制器
    /// </summary>
    /// <param name="twoFactorService">双因素认证服务</param>
    /// <param name="authService">认证服务</param>
    /// <param name="registrationService">注册服务</param>
    /// <param name="passwordService">密码服务</param>
    /// <param name="oAuthService">OAuth服务（可选）</param>
    /// <param name="captchaService">验证码服务（可选）</param>
    /// <param name="identityOptions">Identity配置选项（可选）</param>
    /// <param name="configuration">配置（可选）</param>
    /// <param name="identityPageService">页面生成服务（可选）</param>
    /// <param name="passwordPolicyService">密码策略服务（可选）</param>
    public DefaultAuthController(
        ITwoFactorService twoFactorService,
        IAuthService authService,
        IRegistrationService registrationService,
        IPasswordService passwordService,
        IOAuthService? oAuthService = null,
        ICaptchaService? captchaService = null,
        IOptionsMonitor<IdentityOptions>? identityOptions = null,
        IConfiguration? configuration = null,
        IIdentityPageService? identityPageService = null,
        IPasswordPolicyService? passwordPolicyService = null)
    {
        TwoFactorService = Check.NotNull(twoFactorService);
        AuthService = Check.NotNull(authService);
        RegistrationService = Check.NotNull(registrationService);
        PasswordService = Check.NotNull(passwordService);
        OAuthService = oAuthService;
        CaptchaService = captchaService;
        IdentityOptions = identityOptions;
        Configuration = configuration;
        IdentityPageService = identityPageService;
        PasswordPolicyService = passwordPolicyService;
    }

    /// <summary>
    /// 获取公开认证配置
    /// 供登录页按部署配置（登录方式 / 注册 / 找回 / 第三方登录）决定显隐。匿名可访问，
    /// 仅返回布尔开关与已启用的第三方提供商，不含任何密钥。
    /// </summary>
    /// <returns>认证配置</returns>
    [HttpGet("config")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual ApiResult<AuthConfigDto> GetConfig()
    {
        var result = AuthService.GetAuthConfig();
        return result.ToApiResult();
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    /// <param name="input">登录信息</param>
    /// <returns>JWT Token</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<string>> Login([FromBody] LoginDto input)
    {
        var result = await AuthService.LoginAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 用户登录（带RefreshToken）
    /// </summary>
    /// <param name="input">登录信息</param>
    /// <returns>Token结果（包含AccessToken和RefreshToken）</returns>
    [HttpPost("login-with-refresh-token")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<TokenResult>> LoginWithRefreshToken([FromBody] LoginDto input)
    {
        var result = await AuthService.LoginWithRefreshTokenAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 刷新Token
    /// </summary>
    /// <param name="input">刷新Token请求</param>
    /// <returns>新的Token结果</returns>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<TokenResult>> RefreshToken([FromBody] RefreshTokenDto input)
    {
        var result = await AuthService.RefreshTokenAsync(input.RefreshToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 用户注册（注册成功后自动登录并返回Token）
    /// </summary>
    /// <param name="input">注册信息</param>
    /// <returns>Token结果（包含AccessToken和RefreshToken）</returns>
    [HttpPost("register")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<TokenResult>> Register([FromBody] RegisterDto input)
    {
        var result = await RegistrationService.RegisterAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 忘记密码
    /// </summary>
    /// <param name="email">邮箱地址</param>
    /// <returns>操作结果</returns>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<string>> ForgotPassword([FromBody] ForgotPasswordDto input)
    {
        var result = await PasswordService.ForgotPasswordAsync(input.Email);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重置密码页面（GET）- 用于邮件链接直接访问（兜底方案）
    /// 如果配置了前端URL，会重定向到前端；否则显示HTML表单页面
    /// </summary>
    /// <param name="email">用户邮箱</param>
    /// <param name="token">重置令牌</param>
    /// <returns>重定向或HTML表单页面</returns>
    [HttpGet("reset-password")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<IActionResult> ResetPasswordPage([FromQuery] string email, [FromQuery] string token)
    {
        // 1. 验证参数
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return Content(GenerateResetPasswordResultHtml(false, "缺少必要的参数（email 或 token）"),
                "text/html; charset=utf-8");
        }

        // 2. 如果配置了 ResetPasswordRoute 和 FrontendUrl，重定向到前端
        if (Configuration != null && IdentityOptions != null)
        {
            try
            {
                var frontendUrl = Configuration["App:FrontendUrl"];
                var resetPasswordRoute = IdentityOptions.CurrentValue?.Recovery?.ResetPasswordRoute;

                // 如果配置了 ResetPasswordRoute 和 FrontendUrl，重定向到前端
                if (!string.IsNullOrEmpty(resetPasswordRoute) && !string.IsNullOrEmpty(frontendUrl))
                {
                    var redirectUrl = $"{frontendUrl.TrimEnd('/')}{resetPasswordRoute}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
                    return Redirect(redirectUrl);
                }
            }
            catch
            {
                // 如果获取配置失败，继续使用后端兜底
            }
        }

        // 3. 返回HTML表单页面（兜底）
        return Content(GenerateResetPasswordFormHtml(email, token), "text/html; charset=utf-8");
    }

    /// <summary>
    /// 重置密码（通过Token）
    /// </summary>
    /// <param name="input">重置密码信息</param>
    /// <returns>操作结果</returns>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<string>> ResetPassword([FromBody] ResetPasswordDto input)
    {
        var result = await PasswordService.ResetPasswordByTokenAsync(input.Email, input.Token, input.NewPassword);
        return result.ToApiResult();
    }

    /// <summary>
    /// 发送2FA验证码
    /// </summary>
    /// <param name="input">发送验证码请求</param>
    /// <returns>2FA挑战信息</returns>
    [HttpPost("send-2fa-code")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<TwoFactorChallengeDto>> SendTwoFactorCode([FromBody] SendTwoFactorCodeDto input)
    {
        var result = await AuthService.SendTwoFactorCodeAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 验证2FA并登录
    /// </summary>
    /// <param name="input">2FA验证信息</param>
    /// <returns>Token结果</returns>
    [HttpPost("verify-2fa")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<TokenResult>> VerifyTwoFactor([FromBody] VerifyTwoFactorDto input)
    {
        var result = await AuthService.VerifyTwoFactorAndLoginAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 用户登出（从当前认证用户获取 userId，防止登出他人）
    /// </summary>
    /// <returns>操作结果</returns>
    [HttpPost("logout")]
    [ApiAuthorize]
    [ApiExplorerSettings(GroupName = "user")]
    public virtual async Task<ApiResult<string>> Logout()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<string>("User not authenticated");
        }

        var result = await AuthService.LogoutAsync(CurrentUser.Id.Value);
        return result.ToApiResult();
    }

    /// <summary>
    /// 发起OAuth第三方登录（跳转到第三方登录页面）
    /// </summary>
    /// <param name="provider">OAuth提供者名称（不区分大小写，支持 Google、Microsoft、Facebook、Twitter、GitHub）</param>
    /// <param name="returnUrl">登录成功后的回调地址（可选，前端页面URL）</param>
    /// <returns>重定向到第三方登录页面</returns>
    [HttpGet("oauth/{provider:regex((?i)google|microsoft|facebook|twitter|github)}/login")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<IActionResult> OAuthLogin(string provider, [FromQuery] string? returnUrl = null)
    {
        // 验证 Provider 是否已配置
        var schemeProvider = HttpContext.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        var schemes = await schemeProvider.GetAllSchemesAsync();

        // 规范化 provider 名称（首字母大写，以匹配注册的 scheme 名称）
        var schemeName = NormalizeProviderName(provider);
        var scheme = schemes.FirstOrDefault(s => s.Name.Equals(schemeName, StringComparison.OrdinalIgnoreCase));

        if (scheme == null)
        {
            return new BadRequestObjectResult(BadRequest<string>($"OAuth provider '{provider}' is not configured"));
        }

        // 构建回调处理端点的 URL（OAuth 中间件完成后重定向到这里）
        var callbackHandlerUrl = Url.Action(nameof(OAuthCallbackHandler), new { provider = provider.ToLowerInvariant() });

        // 配置认证属性
        var properties = new AuthenticationProperties
        {
            RedirectUri = callbackHandlerUrl,
            Items =
            {
                ["LoginProvider"] = scheme.Name
            }
        };

        // 保存 returnUrl（如果有）
        if (!string.IsNullOrEmpty(returnUrl))
        {
            properties.Items["returnUrl"] = returnUrl;
        }

        // 发起 Challenge，重定向到第三方登录页面
        return Challenge(properties, scheme.Name);
    }

    /// <summary>
    /// OAuth回调处理端点（OAuth 中间件完成认证后重定向到这里）
    /// 注意：这个端点与 OAuth 中间件的 CallbackPath 不同
    /// CallbackPath 是中间件拦截的路径（如 /auth/oauth/google-callback）
    /// 这个端点是中间件完成后重定向到的路径（如 /auth/oauth/google/callback）
    /// </summary>
    /// <param name="provider">OAuth提供者名称</param>
    /// <returns>OAuth回调结果HTML页面</returns>
    [HttpGet("oauth/{provider:regex((?i)google|microsoft|facebook|twitter|github)}/callback")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<IActionResult> OAuthCallbackHandler(string provider)
    {
        if (OAuthService == null)
        {
            return Content(GenerateOAuthErrorHtml("OAuth service is not available"), "text/html; charset=utf-8");
        }

        try
        {
            // 从 Identity.External scheme 获取认证结果
            var authenticateResult = await HttpContext.AuthenticateAsync("Identity.External");

            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                var errorMessage = authenticateResult.Failure?.Message ?? "OAuth authentication failed";
                return Content(GenerateOAuthErrorHtml(errorMessage), "text/html; charset=utf-8");
            }

            // 获取 returnUrl
            var returnUrl = authenticateResult.Properties?.Items.ContainsKey("returnUrl") == true
                ? authenticateResult.Properties.Items["returnUrl"]
                : null;

            // 添加IP地址和UserAgent到Claims
            var claims = authenticateResult.Principal.Claims.ToList();
            claims.Add(new Claim("ip_address", HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""));
            claims.Add(new Claim("user_agent", HttpContext.Request.Headers["User-Agent"].ToString()));

            var claimsPrincipal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, authenticateResult.Principal.Identity?.AuthenticationType));

            // 处理OAuth回调
            var result = await OAuthService.HandleOAuthCallbackAsync(provider.ToLowerInvariant(), claimsPrincipal);

            if (!result.Succeeded)
            {
                return Content(GenerateOAuthErrorHtml(result.Message ?? "OAuth callback failed"), "text/html; charset=utf-8");
            }

            // 清除 Identity.External cookie
            await HttpContext.SignOutAsync("Identity.External");

            // 生成回调HTML
            var html = GenerateOAuthCallbackHtml(result.Data!, returnUrl);
            return Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            return Content(GenerateOAuthErrorHtml($"OAuth callback error: {ex.Message}"), "text/html; charset=utf-8");
        }
    }

    /// <summary>
    /// 规范化 OAuth 提供者名称（首字母大写）
    /// </summary>
    private static string NormalizeProviderName(string provider)
    {
        if (string.IsNullOrEmpty(provider)) return provider;
        return char.ToUpperInvariant(provider[0]) + provider[1..].ToLowerInvariant();
    }

    /// <summary>
    /// 生成OAuth回调HTML页面（通过postMessage传递结果给父窗口）
    /// </summary>
    protected virtual string GenerateOAuthCallbackHtml(OAuthCallbackResultDto result, string? returnUrl = null)
        => IdentityPageService?.GenerateOAuthCallbackHtml(result, returnUrl)
           ?? throw new InvalidOperationException("IIdentityPageService is not registered. Register it in your module's ConfigureServicesAsync.");

    /// <summary>
    /// 生成OAuth错误HTML页面
    /// </summary>
    protected virtual string GenerateOAuthErrorHtml(string errorMessage)
        => IdentityPageService?.GenerateOAuthErrorHtml(errorMessage)
           ?? throw new InvalidOperationException("IIdentityPageService is not registered. Register it in your module's ConfigureServicesAsync.");

    /// <summary>
    /// 获取验证码
    /// </summary>
    /// <param name="purpose">用途（login, register）</param>
    /// <returns>验证码图片和ID</returns>
    [HttpGet("captcha/{purpose}")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<IActionResult> GetCaptcha(string purpose)
    {
        if (CaptchaService == null)
        {
            return new ObjectResult(Error<object>("Captcha service is not available", 503)) { StatusCode = 503 };
        }

        if (string.IsNullOrEmpty(purpose) || (purpose != "login" && purpose != "register"))
        {
            return new BadRequestObjectResult(BadRequest<object>("Invalid captcha purpose. Use 'login' or 'register'."));
        }

        var result = await CaptchaService.GenerateAsync(purpose);

        // 返回验证码图片，并在响应头中包含 captchaId
        Response.Headers["X-Captcha-Id"] = result.CaptchaId;
        Response.Headers["X-Captcha-Expires"] = result.ExpirationSeconds.ToString();

        return File(result.ImageBytes, "image/png");
    }

    /// <summary>
    /// 获取验证码信息（返回JSON格式，包含Base64图片）
    /// </summary>
    /// <param name="purpose">用途（login, register）</param>
    /// <returns>验证码信息</returns>
    [HttpGet("captcha/{purpose}/json")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<CaptchaDto>> GetCaptchaJson(string purpose)
    {
        if (CaptchaService == null)
        {
            return Error<CaptchaDto>("Captcha service is not available", 503);
        }

        if (string.IsNullOrEmpty(purpose) || (purpose != "login" && purpose != "register"))
        {
            return BadRequest<CaptchaDto>("Invalid captcha purpose. Use 'login' or 'register'.");
        }

        var result = await CaptchaService.GenerateAsync(purpose);

        return Ok(new CaptchaDto
        {
            CaptchaId = result.CaptchaId,
            ImageBase64 = Convert.ToBase64String(result.ImageBytes),
            ExpirationSeconds = result.ExpirationSeconds
        });
    }

    /// <summary>
    /// 发送快速注册验证码
    /// </summary>
    /// <param name="input">发送请求</param>
    /// <returns>操作结果</returns>
    [HttpPost("quick-register/send-code")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<string>> SendQuickRegisterCode([FromBody] SendQuickRegisterCodeDto input)
    {
        var result = await RegistrationService.SendQuickRegisterCodeAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 快速注册（无需密码）
    /// </summary>
    /// <param name="input">注册信息</param>
    /// <returns>注册结果</returns>
    [HttpPost("quick-register")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<QuickRegisterResultDto>> QuickRegister([FromBody] QuickRegisterDto input)
    {
        var result = await RegistrationService.QuickRegisterAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 设置密码（快速注册后使用）
    /// </summary>
    /// <param name="input">设置密码信息</param>
    /// <returns>操作结果</returns>
    [HttpPost("set-password")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<string>> SetPassword([FromBody] SetPasswordDto input)
    {
        var result = await RegistrationService.SetPasswordAsync(input);
        return result.ToApiResult();
    }

    #region 验证码登录

    /// <summary>
    /// 发送验证码登录验证码
    /// 向指定邮箱或手机号发送验证码，用于验证码登录
    /// </summary>
    /// <param name="input">发送请求（邮箱/手机号 + 验证类型）</param>
    /// <returns>操作结果</returns>
    [HttpPost("code-login/send-code")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<string>> SendCodeLoginCode([FromBody] SendCodeLoginCodeDto input)
    {
        var result = await AuthService.SendCodeLoginCodeAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 验证码登录
    /// 使用邮箱/手机号和验证码登录，首次登录且快速注册开启时自动注册
    /// </summary>
    /// <param name="input">登录请求（邮箱/手机号 + 验证码 + 验证类型）</param>
    /// <returns>登录结果（包含 Token 和是否需要设置密码）</returns>
    [HttpPost("code-login")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<CodeLoginResultDto>> CodeLogin([FromBody] CodeLoginDto input)
    {
        var result = await AuthService.CodeLoginAsync(input);
        return result.ToApiResult();
    }

    #endregion

    #region 验证码找回密码

    /// <summary>
    /// 发送密码找回验证码
    /// 向指定邮箱或手机号发送验证码，用于重置密码
    /// </summary>
    /// <param name="input">发送请求（邮箱/手机号 + 验证类型）</param>
    /// <returns>操作结果</returns>
    [HttpPost("password-recovery/send-code")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<string>> SendPasswordRecoveryCode([FromBody] SendPasswordRecoveryCodeDto input)
    {
        var result = await AuthService.SendPasswordRecoveryCodeAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 验证码重置密码
    /// 使用邮箱/手机号和验证码重置密码
    /// </summary>
    /// <param name="input">重置请求（邮箱/手机号 + 验证码 + 新密码 + 验证类型）</param>
    /// <returns>操作结果</returns>
    [HttpPost("password-recovery/reset")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<string>> ResetPasswordByCode([FromBody] ResetPasswordByCodeDto input)
    {
        var result = await AuthService.ResetPasswordByCodeAsync(input);
        return result.ToApiResult();
    }

    #endregion

    /// <summary>
    /// 重发邮箱确认邮件
    /// 支持通过用户ID或邮箱地址重发确认邮件
    /// </summary>
    /// <param name="input">重发请求（包含用户ID或邮箱）</param>
    /// <returns>操作结果</returns>
    [HttpPost("resend-email-confirmation")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<string>> ResendEmailConfirmation([FromBody] ResendEmailConfirmationDto input)
    {
        var result = await RegistrationService.ResendEmailConfirmationAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 确认邮箱（后端API回调方式，类似OAuth回调）
    /// 用户点击邮件中的链接后，重定向到此端点进行邮箱确认
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="token">邮箱确认令牌（URL安全的Base64编码）</param>
    /// <param name="returnUrl">确认成功后的重定向地址（可选）</param>
    /// <returns>确认结果HTML页面或重定向</returns>
    [HttpGet("confirm-email")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<IActionResult> ConfirmEmail([FromQuery] Guid userId, [FromQuery] string token, [FromQuery] string? returnUrl = null)
    {
        var result = await RegistrationService.ConfirmEmailAsync(userId, token);

        if (result.Succeeded)
        {
            // 确认成功
            if (!string.IsNullOrEmpty(returnUrl))
            {
                // 重定向到前端页面（带成功标记）
                var redirectUrl = AppendQueryParam(returnUrl, "emailConfirmed", "true");
                return Redirect(redirectUrl);
            }

            // 返回成功HTML页面
            return Content(GenerateEmailConfirmationResultHtml(true, "邮箱确认成功！您现在可以使用所有功能。"), "text/html; charset=utf-8");
        }
        else
        {
            // 确认失败
            if (!string.IsNullOrEmpty(returnUrl))
            {
                // 重定向到前端页面（带错误信息）
                var redirectUrl = AppendQueryParam(returnUrl, "emailConfirmError", result.Message ?? "确认失败");
                return Redirect(redirectUrl);
            }

            // 返回失败HTML页面
            return Content(GenerateEmailConfirmationResultHtml(false, result.Message ?? "邮箱确认失败"), "text/html; charset=utf-8");
        }
    }

    /// <summary>
    /// 评估密码强度（匿名接口，供前端注册/修改密码时实时反馈）
    /// </summary>
    /// <param name="password">待评估的密码</param>
    /// <returns>密码强度评估结果（评分、等级、建议）</returns>
    [HttpPost("password-strength")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual ApiResult<PasswordStrengthResult> EvaluatePasswordStrength([FromBody] string password)
    {
        if (PasswordPolicyService == null)
        {
            return ApiResult<PasswordStrengthResult>.Error("Password policy service is not available");
        }

        var result = PasswordPolicyService.EvaluatePasswordStrength(password);
        return ApiResult<PasswordStrengthResult>.Ok(result);
    }

    /// <summary>
    /// 向URL追加查询参数
    /// </summary>
    private static string AppendQueryParam(string url, string key, string value)
    {
        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }

    /// <summary>
    /// 生成邮箱确认结果HTML页面
    /// </summary>
    protected virtual string GenerateEmailConfirmationResultHtml(bool success, string message)
        => IdentityPageService?.GenerateEmailConfirmationResultHtml(success, message)
           ?? throw new InvalidOperationException("IIdentityPageService is not registered. Register it in your module's ConfigureServicesAsync.");

    /// <summary>
    /// 生成重置密码表单HTML页面
    /// </summary>
    protected virtual string GenerateResetPasswordFormHtml(string email, string token)
        => IdentityPageService?.GenerateResetPasswordFormHtml(email, token)
           ?? throw new InvalidOperationException("IIdentityPageService is not registered. Register it in your module's ConfigureServicesAsync.");

    /// <summary>
    /// 生成重置密码结果HTML页面
    /// </summary>
    protected virtual string GenerateResetPasswordResultHtml(bool success, string message)
        => IdentityPageService?.GenerateResetPasswordResultHtml(success, message)
           ?? throw new InvalidOperationException("IIdentityPageService is not registered. Register it in your module's ConfigureServicesAsync.");

    // 注意：认证操作不是CRUD操作，不提供钩子方法
    // 如需扩展，请使用重写方法或事件系统（服务层已发布事件）
}