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

    public PasswordService(
        UserManager<User> userManager,
        IOptions<IdentityOptions> identityOptions,
        IServiceProvider serviceProvider,
        IEventBus? eventBus = null,
        IConfiguration? configuration = null,
        IPasswordPolicyService? passwordPolicyService = null,
        ICurrentUser? currentUser = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _identityOptions = Check.NotNull(identityOptions).Value;
        _eventBus = eventBus;
        _configuration = configuration;
        _passwordPolicyService = passwordPolicyService;
        _currentUser = currentUser;
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
                    FrontendUrl = _configuration?["App:FrontendUrl"],
                    SiteName = _configuration?["App:SiteName"]
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
        var user = await _userManager.FindByGuidAsync(userId);
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

    public async Task<Result> ResetPasswordByAdminAsync(Guid userId, string newPassword)
    {
        var user = await _userManager.FindByGuidAsync(userId);
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
