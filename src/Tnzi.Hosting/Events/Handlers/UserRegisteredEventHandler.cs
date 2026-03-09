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
    private readonly IOptions<IdentityOptions>? _identityOptions;
    private readonly IRegistrationService? _registrationService;
    private readonly ILogger<UserRegisteredEventHandler> _logger;

    public UserRegisteredEventHandler(
        INotificationService notificationService,
        ILogger<UserRegisteredEventHandler> logger,
        ISettingService? settingService = null,
        IOptions<IdentityOptions>? identityOptions = null,
        IRegistrationService? registrationService = null)
    {
        _notificationService = Check.NotNull(notificationService);
        _settingService = settingService;
        _identityOptions = identityOptions;
        _registrationService = registrationService;
        _logger = logger;
    }

    public async Task HandleAsync(UserRegisteredEvent @event, CancellationToken cancellationToken = default)
    {
        // 如果没有邮箱地址，跳过发送
        if (string.IsNullOrWhiteSpace(@event.Email))
        {
            _logger.LogDebug("User {UserId} has no email address, skipping welcome email", @event.UserId);
            return;
        }

        try
        {
            // 1. 获取应用配置
            var (appName, frontendUrl, apiBaseUrl) = await GetApplicationConfigAsync();

            // 2. 获取注册配置
            var requireEmailConfirmation = _identityOptions?.Value?.Registration?.RequireConfirmedEmail ?? false;

            // 3. 生成邮箱确认链接（如果需要）
            var confirmationUrl = requireEmailConfirmation
                ? await GenerateConfirmationUrlAsync(@event.UserId, apiBaseUrl, frontendUrl)
                : string.Empty;

            // 4. 发送欢迎邮件
            await SendWelcomeEmailAsync(@event, appName, requireEmailConfirmation, confirmationUrl, cancellationToken);

            _logger.LogInformation("Welcome email sent to user {UserId} ({Email})", @event.UserId, @event.Email);
        }
        catch (Exception ex)
        {
            // 记录错误但不抛出（事件处理器的错误不应影响主业务流程）
            _logger.LogError(ex, "Failed to send welcome email to user {UserId} ({Email})", @event.UserId, @event.Email);
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
    /// 生成邮箱确认链接
    /// </summary>
    private async Task<string> GenerateConfirmationUrlAsync(Guid userId, string apiBaseUrl, string frontendUrl)
    {
        if (_registrationService == null)
        {
            _logger.LogWarning("RegistrationService is not available, cannot generate confirmation URL");
            return string.Empty;
        }

        try
        {
            var tokenResult = await _registrationService.GenerateEmailConfirmationTokenAsync(userId);
            if (!tokenResult.Succeeded || string.IsNullOrEmpty(tokenResult.Data))
            {
                _logger.LogWarning("Failed to generate email confirmation token for user {UserId}: {Error}",
                    userId, tokenResult.Message);
                return string.Empty;
            }

            // 确定 API 基础地址
            var baseUrl = ResolveApiBaseUrl(apiBaseUrl, frontendUrl);
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogWarning("Cannot determine API base URL for email confirmation link");
                return string.Empty;
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generating email confirmation token for user {UserId}", userId);
            return string.Empty;
        }
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