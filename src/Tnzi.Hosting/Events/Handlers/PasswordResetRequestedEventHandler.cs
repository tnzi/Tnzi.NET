using Tnzi.Notification.Metadata;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Hosting.Events.Handlers;

/// <summary>
/// 密码重置请求事件处理器
/// 处理密码重置请求并发送重置密码邮件
/// </summary>
/// <remarks>
/// ★★ <strong>「发不出去就别发」比「发一封残缺的」重要</strong>（与 <see cref="UserRegisteredEventHandler"/> 同一条线）：
/// 重置链接拼不出来时抛异常，而不是发一封按钮 href 为空、可复制链接为空的邮件。
/// 链接生成在发信之前，抛出去让总线重试不会重复发信。
/// </remarks>
public class PasswordResetRequestedEventHandler : IEventHandler<PasswordResetRequestedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ISettingService? _settingService;
    private readonly IOptionsMonitor<IdentityOptions>? _identityOptions;
    private readonly IResetPasswordUrlGenerator? _urlGenerator;
    private readonly ILogger<PasswordResetRequestedEventHandler> _logger;
    private readonly IConfiguration? _configuration;

    public PasswordResetRequestedEventHandler(
        INotificationService notificationService,
        ILogger<PasswordResetRequestedEventHandler> logger,
        ISettingService? settingService = null,
        IOptionsMonitor<IdentityOptions>? identityOptions = null,
        IResetPasswordUrlGenerator? urlGenerator = null,
        IConfiguration? configuration = null)
    {
        _notificationService = Check.NotNull(notificationService);
        _settingService = settingService;
        _identityOptions = identityOptions;
        _urlGenerator = urlGenerator;
        _logger = Check.NotNull(logger);
        _configuration = configuration;
    }

    public async Task HandleAsync(PasswordResetRequestedEvent @event, CancellationToken cancellationToken = default)
    {
        // 如果没有邮箱地址，跳过发送
        if (string.IsNullOrWhiteSpace(@event.Email))
        {
            _logger.LogDebug("Password reset requested for user {UserId} but no email address provided", @event.UserId);
            return;
        }

        // 不再吞异常：发送失败应冒泡给事件总线，由其错误隔离 + 重试 + DLQ 兜底。
        // 这句对 Result 形态的失败同样成立：CreateAndSendAsync 对业务失败不抛而是返回 Fail，
        // 由 NotificationDispatch.SendOrThrowAsync 把它变成异常（见该类的注释）。
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

    /// <summary>
    /// 获取应用程序配置
    /// </summary>
    private async Task<(string AppName, string FrontendUrl, string ApiBaseUrl)> GetApplicationConfigAsync()
    {
        var appName = "Tnzi.NET";
        // 前端 origin 与 Identity 那几处走同一个解析器（System:FrontendUrl，旧键 App:FrontendUrl 回退并告警）：
        // 此前这里只读 ApplicationOptions.FrontendUrl 而 Identity 只读 App:*，消费方配一个键就以为全配了。
        // 设置中心把库里的值投影回 IConfiguration，所以按键读到的就是运行期生效值；没注入 IConfiguration 时
        // 退回 ApplicationOptions 那份。
        var frontendUrl = FrontendUrlResolver.Resolve(_configuration, _logger) ?? string.Empty;
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
            if (frontendUrl.Length == 0)
            {
                frontendUrl = appOptions.FrontendUrl?.TrimEnd('/') ?? string.Empty;
            }

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
    /// <remarks>
    /// ★★★ <strong>生成不出来时必须抛，不能返回空串。</strong>
    /// 模板对 <c>ResetUrl</c> 不做判空：按钮的 href 与「粘贴到浏览器」那行会原样输出空串，
    /// 用户点「忘记密码」收到一封什么都点不了的邮件，Identity 侧令牌已签发且计入频控，
    /// 而日志里是一条 Warning 加一条「sent」。不加载 <c>Tnzi.System</c>（没有 <c>ISettingService</c>）
    /// 或没配 <c>System:ApiBaseUrl</c> / <c>System:FrontendUrl</c>（键见 <see cref="ApplicationUrlSettingKeys"/>）的宿主一定走到这里。
    /// 抛出去则由事件总线的错误隔离 + 重试 + 死信兜底；这一步在发信之前，重试不会重复发信。
    /// </remarks>
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

        throw new TnziException(
            "Password reset link cannot be built: register IResetPasswordUrlGenerator, "
            + $"or set {ApplicationUrlSettingKeys.ApiBaseUrl} / {ApplicationUrlSettingKeys.FrontendUrl} "
            + "(with Identity:Recovery:ResetPasswordRoute for a frontend link).");
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
            // 事务性：对方自己点了「忘记密码」，退订营销邮件不该把这条也一起挡掉。
            IsTransactional = true,
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

        await NotificationDispatch.SendOrThrowAsync(_notificationService, request, "password reset email", cancellationToken);
    }
}