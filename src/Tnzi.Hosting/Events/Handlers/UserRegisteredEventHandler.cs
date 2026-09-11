using Tnzi.Notification.Metadata;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Hosting.Events.Handlers;

/// <summary>
/// 用户注册事件处理器
/// 处理用户注册后发送欢迎邮件（含邮箱确认链接）
/// </summary>
public class UserRegisteredEventHandler : IEventHandler<UserRegisteredEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ISettingService? _settingService;
    private readonly IOptionsMonitor<IdentityOptions>? _identityOptions;
    private readonly IRegistrationService? _registrationService;
    private readonly ILogger<UserRegisteredEventHandler> _logger;

    public UserRegisteredEventHandler(
        INotificationService notificationService,
        ILogger<UserRegisteredEventHandler> logger,
        ISettingService? settingService = null,
        IOptionsMonitor<IdentityOptions>? identityOptions = null,
        IRegistrationService? registrationService = null)
    {
        _notificationService = Check.NotNull(notificationService);
        _settingService = settingService;
        _identityOptions = identityOptions;
        _registrationService = registrationService;
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(UserRegisteredEvent @event, CancellationToken cancellationToken = default)
    {
        // 如果没有邮箱地址，跳过发送
        if (string.IsNullOrWhiteSpace(@event.Email))
        {
            _logger.LogDebug("User {UserId} has no email address, skipping welcome email", @event.UserId);
            return;
        }

        // 不再吞异常：发送失败应冒泡给事件总线，由其错误隔离 + 重试 + DLQ 兜底
        // 1. 获取应用配置
        var (appName, frontendUrl, apiBaseUrl) = await GetApplicationConfigAsync();

        // 2. 获取注册配置
        var requireEmailConfirmation = _identityOptions?.CurrentValue?.Registration?.RequireConfirmedEmail ?? false;

        // 3. 生成邮箱确认链接（如果需要）
        var confirmationUrl = requireEmailConfirmation
            ? await GenerateConfirmationUrlAsync(@event.UserId, apiBaseUrl, frontendUrl)
            : string.Empty;

        // 4. 发送欢迎邮件
        await SendWelcomeEmailAsync(@event, appName, requireEmailConfirmation, confirmationUrl, cancellationToken);

        _logger.LogInformation("Welcome email sent to user {UserId} ({Email})", @event.UserId, @event.Email);
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
    /// 生成邮箱确认链接
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>生成不出来时必须抛，不能返回空串。</strong>
    /// 模板里判的是 <c>RequireEmailConfirmation &amp;&amp; !string.IsNullOrEmpty(ConfirmationUrl)</c>，
    /// 所以一个空串会让欢迎邮件<b>安静地</b>换成「不需要确认」那一版：
    /// 在 <c>RequireConfirmedEmail=true</c> 的部署里，这个用户从此登不进去、
    /// 收不到确认链接，也不会有任何东西再试一次 —— 而日志里只有一条 Warning，
    /// 邮件本身发送成功。
    /// 抛出去则由事件总线的错误隔离 + 重试 + 死信兜底，这也是本文件顶部那条注释的原意。
    /// <para>
    /// ★ 重试不会造成重复发信：这一步<b>在发信之前</b>，失败时那封邮件根本没有发出去。
    /// </para>
    /// </remarks>
    private async Task<string> GenerateConfirmationUrlAsync(Guid userId, string apiBaseUrl, string frontendUrl)
    {
        if (_registrationService == null)
        {
            throw new TnziException(
                "Email confirmation is required but IRegistrationService is not available; "
                + "load Tnzi.Identity or turn off Identity:Registration:RequireConfirmedEmail.");
        }

        // 不包 try/catch：异常照原样冒泡，事件总线负责隔离、重试与死信。
        var tokenResult = await _registrationService.GenerateEmailConfirmationTokenAsync(userId);
        if (!tokenResult.Succeeded || string.IsNullOrEmpty(tokenResult.Data))
        {
            throw new TnziException(
                $"Failed to generate the email confirmation token for user {userId}: {tokenResult.Message}");
        }

        // 确定 API 基础地址
        var baseUrl = ResolveApiBaseUrl(apiBaseUrl, frontendUrl);
        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new TnziException(
                "Email confirmation is required but no API base URL is configured; "
                + "set Application:ApiBaseUrl (or Application:FrontendUrl) so the confirmation link can be built.");
        }

        // 构建确认链接
        var confirmationUrl = $"{baseUrl}/auth/confirm-email?userId={userId}&token={tokenResult.Data}";

        // 添加返回地址（如果有前端URL）
        if (!string.IsNullOrEmpty(frontendUrl))
        {
            confirmationUrl += $"&returnUrl={Uri.EscapeDataString(frontendUrl.TrimEnd('/'))}";
        }

        _logger.LogDebug("Generated email confirmation URL for user {UserId}", userId);
        return confirmationUrl;
    }

    /// <summary>
    /// 解析 API 基础地址
    /// </summary>
    private static string ResolveApiBaseUrl(string apiBaseUrl, string frontendUrl)
    {
        // 优先使用配置的 API 基础地址
        if (!string.IsNullOrEmpty(apiBaseUrl))
        {
            return apiBaseUrl;
        }

        // 如果没有配置，从前端 URL 提取（假设同源部署）
        if (!string.IsNullOrEmpty(frontendUrl))
        {
            try
            {
                var uri = new Uri(frontendUrl);
                return $"{uri.Scheme}://{uri.Authority}";
            }
            catch
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 发送欢迎邮件
    /// </summary>
    private async Task SendWelcomeEmailAsync(
        UserRegisteredEvent @event,
        string appName,
        bool requireEmailConfirmation,
        string confirmationUrl,
        CancellationToken cancellationToken)
    {
        var templateVariables = new Dictionary<string, object>
        {
            ["UserName"] = @event.UserName,
            ["AppName"] = appName,
            ["RequireEmailConfirmation"] = requireEmailConfirmation,
            ["ConfirmationUrl"] = confirmationUrl
        };

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            // 事务性：注册确认 / 邮箱验证，不验证就用不了账号。
            IsTransactional = true,
            TemplateName = "WelcomeEmail",
            IsHtml = true,
            SendImmediately = true,
            MaxRetryCount = 3,
            TemplateVariables = templateVariables,
            Recipients =
            [
                new RecipientInput
                {
                    Address = @event.Email!,
                    Name = @event.UserName
                }
            ]
        };

        await _notificationService.CreateAndSendAsync(request, cancellationToken);
    }
}