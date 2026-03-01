
namespace Tnzi.Identity.Controllers;

/// <summary>
/// 用户个人资料控制器基类
/// 提供获取当前用户信息、更新用户信息、修改密码、会话管理、2FA、登录历史等API端点，所有方法支持重写
/// </summary>
[ApiExplorerSettings(GroupName = "user")]
[ApiAuthorize]  // 只需要登录即可
public abstract class UserProfileControllerBase : ApiControllerBase
{
    protected readonly IUserService UserService;
    protected readonly IPasswordService PasswordService;
    protected readonly ISessionService? SessionService;
    protected readonly IUserLoginService? UserLoginService;
    protected readonly ITwoFactorService? TwoFactorService;
    protected readonly ILoginLogService? LoginLogService;
    protected readonly IUserDetailService? UserDetailService;
    protected readonly IOAuthService? OAuthService;

    /// <summary>
    /// 初始化用户个人资料控制器基类
    /// </summary>
    protected UserProfileControllerBase(
        IUserService userService,
        IPasswordService passwordService,
        ISessionService? sessionService = null,
        IUserLoginService? userLoginService = null,
        ITwoFactorService? twoFactorService = null,
        ILoginLogService? loginLogService = null,
        IUserDetailService? userDetailService = null,
        IOAuthService? oAuthService = null)
    {
        UserService = Check.NotNull(userService);
        PasswordService = Check.NotNull(passwordService);
        SessionService = sessionService;
        UserLoginService = userLoginService;
        TwoFactorService = twoFactorService;
        LoginLogService = loginLogService;
        UserDetailService = userDetailService;
        OAuthService = oAuthService;
    }

    /// <summary>
    /// 获取当前用户信息
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<UserDto>> GetCurrentUser()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<UserDto>("User not authenticated");
        }

        var userResult = await UserService.GetByIdAsync(CurrentUser.Id.Value);
        if (!userResult.Succeeded || userResult.Data == null)
        {
            return NotFound<UserDto>(userResult.Message ?? "User not found");
        }

        return Ok(userResult.Data);
    }

    /// <summary>
    /// 更新当前用户信息
    /// </summary>
    [HttpPut]
    public virtual async Task<ApiResult<UserDto>> UpdateCurrentUser([FromBody] UpdateUserDto input)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<UserDto>("User not authenticated");
        }

        var result = await UserService.UpdateAsync(CurrentUser.Id.Value, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    [HttpPost("change-password")]
    public virtual async Task<ApiResult> ChangePassword([FromBody] ChangePasswordDto input)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        var result = await PasswordService.ChangePasswordAsync(
            CurrentUser.Id.Value,
            input.CurrentPassword,
            input.NewPassword);
        return result.ToApiResult();
    }

    #region 会话管理

    /// <summary>
    /// 获取当前用户的所有会话
    /// </summary>
    [HttpGet("sessions")]
    public virtual async Task<ApiResult<IEnumerable<UserSessionDto>>> GetMySessions()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<IEnumerable<UserSessionDto>>("User not authenticated");
        }

        if (SessionService == null)
        {
            return Error<IEnumerable<UserSessionDto>>("Session service is not available", 503);
        }

        var result = await SessionService.GetUserSessionsAsync(CurrentUser.Id.Value);
        return result.ToApiResult();
    }

    /// <summary>
    /// 撤销指定会话
    /// </summary>
    [HttpDelete("sessions/{sessionId}")]
    public virtual async Task<ApiResult> RevokeMySession(Guid sessionId)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        if (SessionService == null)
        {
            return Error("Session service is not available", 503);
        }

        var result = await SessionService.RevokeSessionAsync(sessionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 撤销当前用户的所有会话
    /// </summary>
    [HttpDelete("sessions")]
    public virtual async Task<ApiResult> RevokeAllMySessions()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        if (SessionService == null)
        {
            return Error("Session service is not available", 503);
        }

        var result = await SessionService.RevokeAllSessionsAsync(CurrentUser.Id.Value);
        return result.ToApiResult();
    }

    #endregion

    #region OAuth 关联账户

    /// <summary>
    /// 获取当前用户的关联登录账户
    /// </summary>
    [HttpGet("linked-accounts")]
    public virtual async Task<ApiResult<IEnumerable<UserLoginDto>>> GetLinkedAccounts()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<IEnumerable<UserLoginDto>>("User not authenticated");
        }

        if (UserLoginService == null)
        {
            return Error<IEnumerable<UserLoginDto>>("User login service is not available", 503);
        }

        var result = await UserLoginService.GetUserLoginsAsync(CurrentUser.Id.Value);
        return result.ToApiResult();
    }

    /// <summary>
    /// 解除关联第三方登录账户
    /// </summary>
    [HttpDelete("linked-accounts/{provider}")]
    public virtual async Task<ApiResult> UnlinkAccount(string provider)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        if (OAuthService == null)
        {
            return Error("OAuth service is not available", 503);
        }

        var result = await OAuthService.UnlinkOAuthAccountAsync(CurrentUser.Id.Value, provider);
        return result.ToApiResult();
    }

    #endregion

    #region 双因素认证

    /// <summary>
    /// 获取2FA状态
    /// </summary>
    [HttpGet("two-factor/status")]
    public virtual async Task<ApiResult<TwoFactorStatusDto>> GetTwoFactorStatus()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<TwoFactorStatusDto>("User not authenticated");
        }

        if (TwoFactorService == null)
        {
            return Error<TwoFactorStatusDto>("Two-factor service is not available", 503);
        }

        var result = await TwoFactorService.GetTwoFactorStatusAsync(CurrentUser.Id.Value);
        return result.ToApiResult();
    }

    /// <summary>
    /// 启用双因素认证
    /// </summary>
    [HttpPost("two-factor/enable")]
    public virtual async Task<ApiResult<string>> EnableTwoFactor([FromBody] EnableTwoFactorDto input)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<string>("User not authenticated");
        }

        if (TwoFactorService == null)
        {
            return Error<string>("Two-factor service is not available", 503);
        }

        var result = await TwoFactorService.EnableTwoFactorAsync(CurrentUser.Id.Value, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 禁用双因素认证
    /// </summary>
    [HttpPost("two-factor/disable")]
    public virtual async Task<ApiResult> DisableTwoFactor()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        if (TwoFactorService == null)
        {
            return Error("Two-factor service is not available", 503);
        }

        var result = await TwoFactorService.DisableTwoFactorAsync(CurrentUser.Id.Value);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取 TOTP 设置信息（生成密钥和二维码 URI）
    /// </summary>
    [HttpPost("two-factor/totp/setup")]
    public virtual async Task<ApiResult<TotpSetupDto>> GetTotpSetup()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<TotpSetupDto>("User not authenticated");
        }

        if (TwoFactorService == null)
        {
            return Error<TotpSetupDto>("Two-factor service is not available", 503);
        }

        var result = await TwoFactorService.GetTotpSetupInfoAsync(CurrentUser.Id.Value);
        return result.ToApiResult();
    }

    /// <summary>
    /// 启用 TOTP（验证用户输入的 code 后启用）
    /// </summary>
    [HttpPost("two-factor/totp/enable")]
    public virtual async Task<ApiResult> EnableTotp([FromBody] EnableTotpDto input)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        if (TwoFactorService == null)
        {
            return Error("Two-factor service is not available", 503);
        }

        var result = await TwoFactorService.EnableTotpAsync(CurrentUser.Id.Value, input.VerificationCode);
        return result.ToApiResult();
    }

    /// <summary>
    /// 禁用 TOTP
    /// </summary>
    [HttpPost("two-factor/totp/disable")]
    public virtual async Task<ApiResult> DisableTotp()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        if (TwoFactorService == null)
        {
            return Error("Two-factor service is not available", 503);
        }

        var result = await TwoFactorService.DisableTotpAsync(CurrentUser.Id.Value);
        return result.ToApiResult();
    }

    #endregion

    #region 登录历史

    /// <summary>
    /// 获取当前用户的登录历史
    /// </summary>
    [HttpGet("login-history")]
    public virtual async Task<ApiResult<IEnumerable<LoginLogDto>>> GetLoginHistory([FromQuery] LoginHistoryQueryDto query)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<IEnumerable<LoginLogDto>>("User not authenticated");
        }

        if (LoginLogService == null)
        {
            return Error<IEnumerable<LoginLogDto>>("Login log service is not available", 503);
        }

        var result = await LoginLogService.GetUserLoginLogsAsync(
            CurrentUser.Id.Value,
            query.StartDate,
            query.EndDate,
            query.IsSuccess);
        return result.ToApiResult();
    }

    #endregion

    #region 用户详情

    /// <summary>
    /// 获取当前用户的详情
    /// </summary>
    [HttpGet("detail")]
    public virtual async Task<ApiResult<UserDetailDto>> GetMyDetail()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<UserDetailDto>("User not authenticated");
        }

        if (UserDetailService == null)
        {
            return Error<UserDetailDto>("User detail service is not available", 503);
        }

        var result = await UserDetailService.GetCurrentAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新当前用户的详情
    /// </summary>
    [HttpPut("detail")]
    public virtual async Task<ApiResult<UserDetailDto>> UpdateMyDetail([FromBody] CreateUserDetailDto input)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<UserDetailDto>("User not authenticated");
        }

        if (UserDetailService == null)
        {
            return Error<UserDetailDto>("User detail service is not available", 503);
        }

        var result = await UserDetailService.UpdateCurrentAsync(input);
        return result.ToApiResult();
    }

    #endregion

    #region 账户管理

    /// <summary>
    /// 停用当前用户的账户
    /// </summary>
    [HttpPost("deactivate")]
    public virtual async Task<ApiResult> DeactivateAccount([FromBody] DeactivateAccountDto? input = null)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        var result = await UserService.DeactivateAccountAsync(CurrentUser.Id.Value, input?.Reason);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除当前用户的账户（软删除，GDPR 合规）
    /// </summary>
    [HttpDelete("account")]
    public virtual async Task<ApiResult> DeleteAccount()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        var result = await UserService.DeleteAccountAsync(CurrentUser.Id.Value);
        return result.ToApiResult();
    }

    /// <summary>
    /// 导出当前用户的个人数据（GDPR 合规）
    /// </summary>
    [HttpGet("personal-data")]
    public virtual async Task<ApiResult<PersonalDataExportDto>> ExportPersonalData()
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized<PersonalDataExportDto>("User not authenticated");
        }

        var result = await UserService.ExportPersonalDataAsync(CurrentUser.Id.Value);
        return result.ToApiResult();
    }

    #endregion

    #region 修改邮箱/手机号

    /// <summary>
    /// 发送修改邮箱验证码（发送到新邮箱）
    /// </summary>
    [HttpPost("change-email/send-code")]
    public virtual async Task<ApiResult> SendChangeEmailCode([FromBody] SendChangeVerificationCodeDto input)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        if (TwoFactorService == null)
        {
            return Error("Two-factor service is not available", 503);
        }

        var result = await TwoFactorService.SendCodeByAddressAsync(input.NewAddress, TwoFactorType.Email);
        return result.ToApiResult();
    }

    /// <summary>
    /// 确认修改邮箱
    /// </summary>
    [HttpPost("change-email/confirm")]
    public virtual async Task<ApiResult> ConfirmChangeEmail([FromBody] ChangeEmailDto input)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        if (TwoFactorService == null)
        {
            return Error("Two-factor service is not available", 503);
        }

        // 验证验证码
        var verifyResult = await TwoFactorService.VerifyCodeByAddressAndMarkUsedAsync(input.NewEmail, input.Code, TwoFactorType.Email);
        if (!verifyResult.Succeeded)
        {
            return Error(verifyResult.Message ?? "Verification failed", verifyResult.Code ?? 400);
        }

        // 更新邮箱（同时设置 EmailConfirmed = true 并发布变更事件）
        var changeResult = await UserService.ChangeEmailAsync(CurrentUser.Id.Value, input.NewEmail);
        return changeResult.ToApiResult();
    }

    /// <summary>
    /// 发送修改手机号验证码（发送到新手机号）
    /// </summary>
    [HttpPost("change-phone/send-code")]
    public virtual async Task<ApiResult> SendChangePhoneCode([FromBody] SendChangeVerificationCodeDto input)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        if (TwoFactorService == null)
        {
            return Error("Two-factor service is not available", 503);
        }

        var result = await TwoFactorService.SendCodeByAddressAsync(input.NewAddress, TwoFactorType.Sms);
        return result.ToApiResult();
    }

    /// <summary>
    /// 确认修改手机号
    /// </summary>
    [HttpPost("change-phone/confirm")]
    public virtual async Task<ApiResult> ConfirmChangePhone([FromBody] ChangePhoneNumberDto input)
    {
        if (CurrentUser?.Id == null)
        {
            return Unauthorized("User not authenticated");
        }

        if (TwoFactorService == null)
        {
            return Error("Two-factor service is not available", 503);
        }

        // 验证验证码
        var verifyResult = await TwoFactorService.VerifyCodeByAddressAndMarkUsedAsync(input.NewPhoneNumber, input.Code, TwoFactorType.Sms);
        if (!verifyResult.Succeeded)
        {
            return Error(verifyResult.Message ?? "Verification failed", verifyResult.Code ?? 400);
        }

        // 更新手机号（同时设置 PhoneNumberConfirmed = true 并发布变更事件）
        var changeResult = await UserService.ChangePhoneNumberAsync(CurrentUser.Id.Value, input.NewPhoneNumber);
        return changeResult.ToApiResult();
    }

    #endregion
}
