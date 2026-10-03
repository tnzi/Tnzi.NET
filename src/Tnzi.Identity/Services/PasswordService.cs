namespace Tnzi.Identity.Services;

/// <summary>
/// 密码服务实现
/// 提供密码重置、修改密码等功能
/// </summary>
public class PasswordService : ApplicationService, IPasswordService
{
    private readonly UserManager<User> _userManager;
    private readonly IdentityOptions _identityOptions;
    private readonly IEventBus? _eventBus;
    private readonly IConfiguration? _configuration;
    private readonly IPasswordPolicyService? _passwordPolicyService;
    private readonly ICurrentUser? _currentUser;
    private readonly ISessionRevocationService? _sessionRevocation;
    private readonly ICurrentTenant? _currentTenant;
    private readonly ICaptchaVerifier? _captchaVerifier;
    private readonly bool _multiTenancyEnabled;
    private readonly IFunctionAuthorizationService? _functionAuthorization;

    public PasswordService(
        UserManager<User> userManager,
        IOptionsSnapshot<IdentityOptions> identityOptions,
        IServiceProvider serviceProvider,
        IEventBus? eventBus = null,
        IConfiguration? configuration = null,
        IPasswordPolicyService? passwordPolicyService = null,
        ICurrentUser? currentUser = null,
        ISessionRevocationService? sessionRevocation = null,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        ICaptchaVerifier? captchaVerifier = null,
        IFunctionAuthorizationService? functionAuthorization = null)
        : base(serviceProvider)
    {
        _functionAuthorization = functionAuthorization;
        _userManager = Check.NotNull(userManager);
        // Scoped 服务：IOptionsSnapshot 每请求重算，Recovery 开关随请求热更新。
        _identityOptions = Check.NotNull(identityOptions).Value;
        _captchaVerifier = captchaVerifier;
        _eventBus = eventBus;
        _configuration = configuration;
        _passwordPolicyService = passwordPolicyService;
        _currentUser = currentUser;
        _sessionRevocation = sessionRevocation;
        _currentTenant = currentTenant;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    /// <summary>
    /// 按 id 取账号，且只取当前租户范围内的（口径见 <see cref="UserTenantScope"/>）；
    /// 范围外与不存在同样返回 <c>null</c>，调用方一律答 404。
    /// 管理员重置密码是这个模块里越权后果最重的一条（顺手撤销对方全部会话），必须与
    /// <c>UserService</c> 同一道裁剪。
    /// </summary>
    private async Task<User?> FindScopedUserAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return null;
        }

        var scope = UserTenantScope.Resolve(_multiTenancyEnabled, _currentTenant, _currentUser ?? CurrentUser);
        if (scope.Contains(user))
        {
            return user;
        }

        LogWarning(
            "Rejected a cross-tenant password operation: user {UserId} belongs to tenant {UserTenantId} but the request is scoped to tenant {TenantId}.",
            user.Id, user.TenantId, scope.TenantId);
        return null;
    }

    /// <summary>
    /// 密码变更之后作废该用户的其它会话与刷新令牌。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ <b>改密码此前不动任何会话。</b>用户中招之后的标准动作就是改密码，而改完之后
    /// 攻击者手里的 access token 照常有效、刷新令牌照常续期 —— 这条最常被依赖的自救路径
    /// 在框架里是无效的，且没有任何迹象表明它无效。
    /// </para>
    /// <para>
    /// ★ <b>两条路径的默认值刻意不同</b>：
    /// <list type="bullet">
    /// <item>找回 / 重置（<paramref name="excludeCurrentSession"/> = false）：全撤。走到这条路径
    /// 通常意味着账号可能已经失陷，此时保留任何一条既有会话都没有道理。</item>
    /// <item>本人改密（<paramref name="excludeCurrentSession"/> = true）：撤其它、留当前。
    /// 这是 ASVS 7.4.3 说的「terminate all <b>other</b> active sessions」；连自己一起踢掉，
    /// 只会让用户觉得改密码是件麻烦事。</item>
    /// </list>
    /// </para>
    /// </remarks>
    private async Task RevokeSessionsAfterPasswordChangeAsync(
        Guid userId, SessionRevocationReason reason, bool excludeCurrentSession)
    {
        if (_sessionRevocation == null)
        {
            LogWarning(
                "ISessionRevocationService is not available; sessions for user {UserId} stay active after {Reason}.",
                userId, reason);
            return;
        }

        Guid? exclude = null;
        if (excludeCurrentSession)
        {
            var raw = _currentUser?.FindClaim(IdentityConstants.ClaimTypeNames.SessionId);
            if (!string.IsNullOrEmpty(raw) && Guid.TryParse(raw, out var sid) && sid != Guid.Empty)
            {
                exclude = sid;
            }
        }

        try
        {
            await _sessionRevocation.RevokeUserSessionsAsync(userId, reason, exclude);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to revoke sessions for user {UserId} after {Reason}.", userId, reason);
        }
    }

    public async Task<Result<string>> ForgotPasswordAsync(ForgotPasswordDto input)
    {
        Check.NotNull(input);

        // 人机验证（启用找回密码验证码时，发出重置邮件之前先过）。无条件要求：这条路径没有失败次数可累计，
        // 而每次调用都真的发一封信。此前它不受任何验证码开关管辖。
        if (_identityOptions.Captcha.EnableCaptchaOnPasswordRecovery)
        {
            if (_captchaVerifier == null)
            {
                Logger.LogError("Captcha is required for password recovery but ICaptchaVerifier is not registered; rejecting.");
                return Fail<string>("Captcha verification is required", 400, ErrorCodes.IDENTITY_CAPTCHA_REQUIRED);
            }

            var verification = await _captchaVerifier.VerifyAsync(ImageCaptchaToken.Resolve(input), CaptchaPurpose.PasswordRecovery);
            // 「未启用，放行」不是「校验通过」：开关已经要求验证码，没有生效的提供商就拒绝。
            if (verification.Skipped)
            {
                Logger.LogError("Captcha is required for password recovery but no captcha provider is enabled; rejecting.");
            }

            if (!verification.Passed || verification.Skipped)
            {
                return Fail<string>("Captcha verification is required", 400, ErrorCodes.IDENTITY_CAPTCHA_REQUIRED,
                    new CaptchaDto { Provider = _captchaVerifier.ProviderName ?? ImageCaptchaProvider.ProviderName });
            }
        }

        return await ForgotPasswordAsync(input.Email);
    }

    public async Task<Result<string>> ForgotPasswordAsync(string email)
    {
        var recoveryOptions = _identityOptions.Recovery;

        // 检查是否启用邮箱找回
        if (!recoveryOptions.EnablePasswordResetByEmail)
        {
            return Fail<string>("Password reset by email is not enabled", 400);
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            // 为了安全，即使用户不存在也返回成功消息
            return Result<string>.Success("If email exists, reset link sent.");
        }

        // ★ 未接受邀请的账号不走找回密码。这条路径**不过登录守卫**（它不签发令牌），
        //   所以 PendingActionsLoginGuard 管不到；不挡在这里，任何知道这个邮箱的人
        //   都能替一个还没入职的账号设上密码。回同一句「若邮箱存在则已发送」而不是报错，
        //   与上面用户不存在时的处理同源：这个端点匿名可达，不能用来试探账号状态。
        if (user.HasPendingAction(PendingUserActions.InvitationPending))
        {
            LogInformation(
                "Password reset suppressed for user {UserId}: invitation not yet accepted.",
                user.Id);
            return Result<string>.Success("If email exists, reset link sent.");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        // 将 token 编码为 URL 安全的 Base64，避免特殊字符在 URL 中被截断
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        // 发布密码重置请求事件，由应用层处理通知发送
        if (_eventBus != null)
        {
            try
            {
                await _eventBus.PublishAsync(new PasswordResetRequestedEvent
                {
                    UserId = user.Id,
                    UserName = user.UserName ?? string.Empty,
                    Email = email,
                    ResetToken = encodedToken,
                    RequestTime = DateTime.UtcNow,
                    FrontendUrl = FrontendUrlResolver.Resolve(_configuration, Logger),
                    SiteName = SiteNameResolver.Resolve(_configuration, Logger)
                }, cancellationToken: default);
            }
            catch (Exception ex)
            {
                // 记录错误但不影响主流程（密码重置流程不应因事件发布失败而中断）
                Logger.LogWarning(ex, "Failed to publish password reset requested event for user {UserId}", user.Id);
            }
        }

        return Result<string>.Success("If email exists, reset link sent.");
    }

    public async Task<Result<string>> ResetPasswordByTokenAsync(string email, string token, string newPassword)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            // 为了安全，不明确告知用户是否存在，但提供更通用的错误信息
            return Fail<string>("Invalid email or token", 400);
        }

        // 解码 Base64Url 编码的 token
        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch
        {
            return Fail<string>("Invalid token format", 400);
        }
        token = decodedToken;

        // 验证密码强度
        if (_passwordPolicyService != null)
        {
            var strengthError = _passwordPolicyService.ValidatePasswordStrength(newPassword);
            if (strengthError != null)
            {
                return Fail<string>(strengthError, 400);
            }

            // 检查密码历史
            var isInHistory = await _passwordPolicyService.CheckPasswordHistoryAsync(user.Id, newPassword);
            if (isInHistory)
            {
                return Fail<string>("Password has been used recently. Please choose a different password.", 400);
            }
        }

        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded) return Fail<string>("Reset failed", 400);

        // 保存密码历史（ResetPasswordAsync 成功后，user.PasswordHash 已更新）
        if (_passwordPolicyService != null && !string.IsNullOrEmpty(user.PasswordHash))
        {
            await _passwordPolicyService.SavePasswordHistoryAsync(user.Id, user.PasswordHash);
        }

        // 走到找回流程说明账号可能已经失陷 —— 全撤，不给任何既有会话留活口。
        await RevokeSessionsAfterPasswordChangeAsync(
            user.Id, SessionRevocationReason.PasswordReset, excludeCurrentSession: false);

        // 发布密码重置事件
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new UserPasswordResetEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                ResetTime = DateTime.UtcNow,
                IsSelfReset = true
            }, cancellationToken: default);
        }

        return Result<string>.Success("Password reset successfully");
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 验证密码强度
        if (_passwordPolicyService != null)
        {
            var strengthError = _passwordPolicyService.ValidatePasswordStrength(newPassword);
            if (strengthError != null)
            {
                return Fail(strengthError ?? "Password is too weak", 400, ErrorCodes.VALIDATION_ERROR);
            }

            // 检查密码历史
            var isInHistory = await _passwordPolicyService.CheckPasswordHistoryAsync(userId, newPassword);
            if (isInHistory)
            {
                return Fail("Password has been used recently. Please choose a different password.", 400, ErrorCodes.VALIDATION_ERROR);
            }
        }

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            return Fail($"Failed to change password: {result.FormatErrors()}", 400, ErrorCodes.IDENTITY_PASSWORD_CHANGE_FAILED);
        }

        // 保存密码历史（ChangePasswordAsync 成功后，user.PasswordHash 已更新）
        if (_passwordPolicyService != null && !string.IsNullOrEmpty(user.PasswordHash))
        {
            await _passwordPolicyService.SavePasswordHistoryAsync(userId, user.PasswordHash);
        }

        // 本人在场改密：踢掉其它设备，保留当前会话（ASVS 7.4.3 的 "all other active sessions"）。
        await RevokeSessionsAfterPasswordChangeAsync(
            userId, SessionRevocationReason.PasswordChanged, excludeCurrentSession: true);

        // 发布密码修改事件
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new UserPasswordChangedEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                ChangedTime = DateTime.UtcNow,
                ChangedBy = _currentUser?.Id
            }, cancellationToken: default);
        }

        LogInformation("Password changed for user: {UserName} (ID: {UserId})", user.UserName ?? string.Empty, userId);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result> ForceSetPasswordAsync(User user, string newPassword, bool revokeExistingSessions = true)
    {
        Check.NotNull(user);

        // 与其它改密路径同一套校验：强度 + 历史。走这条路的人同样不该把密码设成上一个 ——
        // 尤其这条路的常见来源正是「管理员发的临时密码」，允许设回去等于什么都没做。
        if (_passwordPolicyService != null)
        {
            var strengthError = _passwordPolicyService.ValidatePasswordStrength(newPassword);
            if (strengthError != null)
            {
                return Fail(strengthError, 400, ErrorCodes.VALIDATION_ERROR);
            }

            if (await _passwordPolicyService.CheckPasswordHistoryAsync(user.Id, newPassword))
            {
                return Fail(
                    "Password has been used recently. Please choose a different password.",
                    400,
                    ErrorCodes.VALIDATION_ERROR);
            }
        }

        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await _userManager.ResetPasswordAsync(user, resetToken, newPassword);
        if (!reset.Succeeded)
        {
            return Fail(
                $"Failed to set password: {reset.FormatErrors()}",
                400,
                ErrorCodes.IDENTITY_PASSWORD_RESET_FAILED);
        }

        if (_passwordPolicyService != null && !string.IsNullOrEmpty(user.PasswordHash))
        {
            await _passwordPolicyService.SavePasswordHistoryAsync(user.Id, user.PasswordHash);
        }

        // 全撤：走到这条路径的人手上那个密码是别人给的（管理员设的临时密码），
        // 或者是一个已经到期的旧密码 —— 两种情况下保留既有会话都没有道理。
        // 唯一的例外是「此前根本没有密码」，见 revokeExistingSessions 的说明。
        if (revokeExistingSessions)
        {
            await RevokeSessionsAfterPasswordChangeAsync(
                user.Id, SessionRevocationReason.PasswordReset, excludeCurrentSession: false);
        }

        LogInformation("Password set for user {UserId} while discharging a pending action.", user.Id);
        return Ok();
    }

    public async Task<Result> ResetPasswordByAdminAsync(Guid userId, string newPassword, bool requireChangeOnNextLogin = true)
    {
        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 重置别人的密码 = 决定谁能以这个账号登录；非超管对超管做这件事就是拿下超管账号。
        if (await SuperAdminTargetGuard.IsForbiddenAsync(_functionAuthorization, _currentUser?.Id ?? CurrentUser?.Id, userId))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        // 验证密码强度
        if (_passwordPolicyService != null)
        {
            var strengthError = _passwordPolicyService.ValidatePasswordStrength(newPassword);
            if (strengthError != null)
            {
                return Fail(strengthError ?? "Password is too weak", 400, ErrorCodes.VALIDATION_ERROR);
            }

            // 检查密码历史
            var isInHistory = await _passwordPolicyService.CheckPasswordHistoryAsync(userId, newPassword);
            if (isInHistory)
            {
                return Fail("Password has been used recently. Please choose a different password.", 400, ErrorCodes.VALIDATION_ERROR);
            }
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            return Fail($"Failed to reset password: {result.FormatErrors()}", 400, ErrorCodes.IDENTITY_PASSWORD_RESET_FAILED);
        }

        // 保存密码历史（ResetPasswordAsync 成功后，user.PasswordHash 已更新）
        if (_passwordPolicyService != null && !string.IsNullOrEmpty(user.PasswordHash))
        {
            await _passwordPolicyService.SavePasswordHistoryAsync(userId, user.PasswordHash);
        }

        // ★ 把「这是一个临时密码」记成账号身上的一件待办。写失败只告警不改变结果：
        //   密码确实已经重置了，报成失败会让管理员再重置一次（而那次同样会成功）。
        //   代价是这一次的临时性丢了，所以要留下痕迹。
        if (requireChangeOnNextLogin)
        {
            user.PendingActions |= PendingUserActions.ChangePassword;
            var flagged = await _userManager.UpdateAsync(user);
            if (!flagged.Succeeded)
            {
                LogWarning(
                    "Password was reset for user {UserId} but it could not be marked as temporary ({Errors}); "
                    + "the user will NOT be forced to change it on next sign-in.",
                    userId, flagged.FormatErrors());
            }
        }

        // 管理员重置：被重置的那个人不是当前会话的主人，全撤。
        await RevokeSessionsAfterPasswordChangeAsync(
            userId, SessionRevocationReason.PasswordReset, excludeCurrentSession: false);

        // 发布密码重置事件
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new UserPasswordResetEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                ResetTime = DateTime.UtcNow,
                ResetBy = _currentUser?.Id,
                IsSelfReset = false
            }, cancellationToken: default);
        }

        LogInformation("Password reset by admin for user: {UserName} (ID: {UserId})", user.UserName ?? string.Empty, userId);
        return Ok();
    }

}
