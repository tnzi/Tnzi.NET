using Tnzi.Notification.Metadata;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Hosting.Events.Handlers;

/// <summary>
/// 密码重置请求事件处理器
/// 处理密码重置请求并发送重置密码邮件
/// </summary>
public class PasswordResetRequestedEventHandler : IEventHandler<PasswordResetRequestedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ISettingService? _settingService;
    private readonly IOptionsMonitor<IdentityOptions>? _identityOptions;
    private readonly IResetPasswordUrlGenerator? _urlGenerator;
    private readonly ILogger<PasswordResetRequestedEventHandler> _logger;

    public PasswordResetRequestedEventHandler(
        INotificationService notificationService,
        ILogger<PasswordResetRequestedEventHandler> logger,
        ISettingService? settingService = null,
        IOptionsMonitor<IdentityOptions>? identityOptions = null,
        IResetPasswordUrlGenerator? urlGenerator = null)
    {
        _notificationService = Check.NotNull(notificationService);
        _settingService = settingService;
        _identityOptions = identityOptions;
        _urlGenerator = urlGenerator;
        _logger = logger;
    }

    public async Task HandleAsync(PasswordResetRequestedEvent @event, CancellationToken cancellationToken = default)
    {
        // 如果没有邮箱地址，跳过发送
        if (string.IsNullOrWhiteSpace(@event.Email))
        {
            _logger.LogDebug("Password reset requested for user {UserId} but no email address provided", @event.UserId);
            return;
        }

        try
        {
            // 1. 获取应用配置
            var (appName, frontendUrl, apiBaseUrl) = await GetApplicationConfigAsync();

            // 2. 生成重置密码链接
            var resetUrl = GenerateResetPasswordUrl(@event.Email, @event.ResetToken, apiBaseUrl, frontendUrl);

            // 3. 获取过期时间（从配置中读取，默认 30 分钟）
            var expirationMinutes = _identityOptions?.CurrentValue?.Recovery?.ResetTokenExpirationMinutes ?? 30;

            // 4. 发送密码重置邮件
            await SendPasswordResetEmailAsync(@event, appName, resetUrl, expirationMinutes, cancellationToken);

            _logger.LogInformation("Password reset email sent to user {UserId} ({Email})", @event.UserId, @event.Email);
        }
        catch (Exception ex)
        {
            // 记录错误但不抛出（事件处理器的错误不应影响主业务流程）
            _logger.LogError(ex, "Failed to send password reset email to user {UserId} ({Email})", @event.UserId, @event.Email);
        }
    }

    /// <summary>
    /// 获取应用程序配置
    /// </summary>
    private async Task<(string AppName, string FrontendUrl, string ApiBaseUrl)> GetApplicationConfigAsync()
    {
        var appName = "Tnzi.NET";
        var frontendUrl = string.Empty;
        var apiBaseUrl = string.Empty;

        if (_settingService == null)
        {
            return (appName, frontendUrl, apiBaseUrl);
        }

        try
        {
            var appNameResult = await _settingService.GetAppNameAsync();
            appName = appNameResult.Succeeded ? appNameResult.Data ?? "Tnzi" : "Tnzi";

            var appOptions = _settingService.GetApplicationOptions();
            frontendUrl = appOptions.FrontendUrl ?? string.Empty;
            apiBaseUrl = appOptions.ApiBaseUrl?.TrimEnd('/') ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get application settings, using default values");
        }

        return (appName, frontendUrl, apiBaseUrl);
    }

    /// <summary>
    /// 生成重置密码链接
    /// </summary>
    private string GenerateResetPasswordUrl(string email, string token, string apiBaseUrl, string frontendUrl)
    {
        // 1. 优先使用自定义URL生成器（如果注册了）
        if (_urlGenerator != null)
        {
            var customUrl = _urlGenerator.GenerateUrl(email, token,
                string.IsNullOrEmpty(frontendUrl) ? null : frontendUrl,
                string.IsNullOrEmpty(apiBaseUrl) ? null : apiBaseUrl);
            if (!string.IsNullOrEmpty(customUrl))
            {
                _logger.LogDebug("Generated password reset URL using custom generator for email {Email}", email);
                return customUrl;
            }
        }

        // 2. 如果配置了 ResetPasswordRoute，使用前端URL + ResetPasswordRoute
        var resetPasswordRoute = _identityOptions?.CurrentValue?.Recovery?.ResetPasswordRoute;
        if (!string.IsNullOrEmpty(resetPasswordRoute) && !string.IsNullOrEmpty(frontendUrl))
        {
            var resetUrl = $"{frontendUrl.TrimEnd('/')}{resetPasswordRoute}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
            _logger.LogDebug("Generated password reset URL (frontend) for email {Email}", email);
            return resetUrl;
        }

        // 3. 如果未配置 ResetPasswordRoute，使用后端API URL + /auth/reset-password（框架内置兜底方案）
        if (!string.IsNullOrEmpty(apiBaseUrl))
        {
            var resetUrl = $"{apiBaseUrl}/auth/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
            _logger.LogDebug("Generated password reset URL (backend fallback) for email {Email}", email);
            return resetUrl;
        }

        _logger.LogWarning("Cannot determine base URL for password reset link");
        return string.Empty;
    }

    /// <summary>
    /// 发送密码重置邮件
    /// </summary>
    private async Task SendPasswordResetEmailAsync(
        PasswordResetRequestedEvent @event,
        string appName,
        string resetUrl,
        int expirationMinutes,
        CancellationToken cancellationToken)
    {
        var templateVariables = new Dictionary<string, object>
        {
            ["UserName"] = @event.UserName,
            ["AppName"] = appName,
            ["ResetUrl"] = resetUrl,
            ["ExpirationMinutes"] = expirationMinutes
        };

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            TemplateName = "PasswordReset",
            IsHtml = true,
            SendImmediately = true,
            MaxRetryCount = 3,
            TemplateVariables = templateVariables,
            Recipients =
            [
                new RecipientInput
                {
                    Address = @event.Email,
                    Name = @event.UserName
                }
            ]
        };

        await _notificationService.CreateAndSendAsync(request, cancellationToken);
    }
}