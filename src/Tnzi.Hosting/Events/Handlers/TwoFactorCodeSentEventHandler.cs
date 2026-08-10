using Tnzi.Notification.Metadata;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Hosting.Events.Handlers;

/// <summary>
/// 双因素验证码发送事件处理器
/// 处理验证码发送请求并发送邮件或短信
/// </summary>
public class TwoFactorCodeSentEventHandler : IEventHandler<TwoFactorCodeSentEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ISettingService? _settingService;
    private readonly IOptions<IdentityOptions>? _identityOptions;
    private readonly ILogger<TwoFactorCodeSentEventHandler> _logger;

    public TwoFactorCodeSentEventHandler(
        INotificationService notificationService,
        ILogger<TwoFactorCodeSentEventHandler> logger,
        ISettingService? settingService = null,
        IOptions<IdentityOptions>? identityOptions = null)
    {
        _notificationService = Check.NotNull(notificationService);
        _settingService = settingService;
        _identityOptions = identityOptions;
        _logger = logger;
    }

    public async Task HandleAsync(TwoFactorCodeSentEvent @event, CancellationToken cancellationToken = default)
    {
        // 如果没有地址，跳过发送
        if (string.IsNullOrWhiteSpace(@event.Address))
        {
            _logger.LogDebug("Two-factor code sent event received but no address provided for user {UserId}", @event.UserId);
            return;
        }

        // 不再吞异常：发送失败应冒泡给事件总线，由其错误隔离 + 重试 + DLQ 兜底
        var appName = await GetAppNameAsync();

        // 根据类型发送
        if (@event.Type.Equals("Email", StringComparison.OrdinalIgnoreCase))
        {
            await SendEmailCodeAsync(@event, appName, cancellationToken);
            _logger.LogInformation("Two-factor code email sent to {Address} for user {UserId}", @event.Address, @event.UserId);
        }
        else if (@event.Type.Equals("Sms", StringComparison.OrdinalIgnoreCase))
        {
            await SendSmsCodeAsync(@event, appName, cancellationToken);
            _logger.LogInformation("Two-factor code SMS sent to {Address} for user {UserId}", @event.Address, @event.UserId);
        }
        else
        {
            _logger.LogWarning("Unknown two-factor code type: {Type}", @event.Type);
        }
    }

    /// <summary>
    /// 获取应用名称
    /// </summary>
    private async Task<string> GetAppNameAsync()
    {
        if (_settingService == null)
        {
            return "Tnzi";
        }

        try
        {
            var result = await _settingService.GetAppNameAsync();
            return result.Succeeded ? result.Data ?? "Tnzi" : "Tnzi";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get app name, using default");
            return "Tnzi";
        }
    }

    /// <summary>
    /// 发送邮件验证码
    /// </summary>
    private async Task SendEmailCodeAsync(TwoFactorCodeSentEvent @event, string appName, CancellationToken cancellationToken)
    {
        var templateVariables = new Dictionary<string, object>
        {
            ["UserName"] = string.IsNullOrEmpty(@event.UserName) ? "User" : @event.UserName,
            ["AppName"] = appName,
            ["Code"] = @event.Code,
            ["ExpirationMinutes"] = @event.ExpirationMinutes
        };

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            // 事务性：验证码是登录流程的一环，收不到就等于账号进不去。
            IsTransactional = true,
            TemplateName = "TwoFactorCode",
            IsHtml = true,
            SendImmediately = true,
            MaxRetryCount = 3,
            TemplateVariables = templateVariables,
            Recipients =
            [
                new RecipientInput
                {
                    Address = @event.Address,
                    Name = @event.UserName
                }
            ]
        };

        await _notificationService.CreateAndSendAsync(request, cancellationToken);
    }

    /// <summary>
    /// 发送短信验证码
    /// </summary>
    private async Task SendSmsCodeAsync(TwoFactorCodeSentEvent @event, string appName, CancellationToken cancellationToken)
    {
        var templateVariables = new Dictionary<string, object>
        {
            ["AppName"] = appName,
            ["Code"] = @event.Code,
            ["ExpirationMinutes"] = @event.ExpirationMinutes
        };

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Sms,
            // 同上，短信通道亦然。
            IsTransactional = true,
            TemplateName = "TwoFactorCode",
            IsHtml = false,
            SendImmediately = true,
            MaxRetryCount = 3,
            TemplateVariables = templateVariables,
            Recipients =
            [
                new RecipientInput
                {
                    Address = @event.Address,
                    Name = @event.UserName
                }
            ]
        };

        await _notificationService.CreateAndSendAsync(request, cancellationToken);
    }
}