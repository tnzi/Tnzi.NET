using Tnzi.Notification.Metadata;

namespace Tnzi.Hosting.Events.Handlers;

/// <summary>
/// 邀请注册事件处理器：把邀请链接发到被邀请人的邮箱。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <strong>没有它，「邀请」这个动作就是一句空话。</strong>
/// <c>InvitationService</c> 只负责开号、签令牌、发事件；真正把链接送出去的是这里。
/// 少了这个处理器，管理员点「邀请用户」会拿到 200、日志会写 "Invitation issued"、
/// 账号也确实建好了，<strong>而那个人永远收不到任何东西</strong> ——
/// 全链路没有一处报错，最后是那个人自己来问「怎么还没收到邮件」。
/// </para>
/// <para>
/// 与 <see cref="PasswordResetRequestedEventHandler"/> 同一形状、同一位置：
/// 服务层只发事件、由 <c>Tnzi.Hosting</c> 提供默认的发信实现，消费应用要换措辞就换模板、
/// 要换渠道就注册自己的处理器。
/// </para>
/// <para>
/// ★ <strong>不吞异常</strong>：发送失败冒泡给事件总线，由它的错误隔离 + 重试 + DLQ 兜底。
/// 一封没发出去的邀请是需要被重试、也需要有人知道的。
/// </para>
/// </remarks>
public class UserInvitedEventHandler : IEventHandler<UserInvitedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ISettingService? _settingService;
    private readonly ILogger<UserInvitedEventHandler> _logger;

    /// <summary>
    /// 初始化一个 <see cref="UserInvitedEventHandler"/> 类型的新实例。
    /// </summary>
    public UserInvitedEventHandler(
        INotificationService notificationService,
        ILogger<UserInvitedEventHandler> logger,
        ISettingService? settingService = null)
    {
        _notificationService = Check.NotNull(notificationService);
        _logger = Check.NotNull(logger);
        _settingService = settingService;
    }

    /// <inheritdoc />
    public async Task HandleAsync(UserInvitedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);

        if (string.IsNullOrWhiteSpace(@event.Email))
        {
            // 只有手机号的邀请要靠消费应用自己发短信 —— 框架不替它决定短信文案与服务商。
            // 用 Information 而不是 Debug：这条邀请**没有被送出去**，那是需要看得见的事实。
            _logger.LogInformation(
                "User {UserId} was invited without an email address; no invitation email was sent. "
                + "Handle UserInvitedEvent yourself to deliver it by SMS or another channel.",
                @event.UserId);
            return;
        }

        var appName = await GetAppNameAsync();

        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            // 事务性：这是管理员主动发起的账号开通，退订营销邮件不该把它一起挡掉
            // （挡掉的结果是那个人根本进不来）。
            IsTransactional = true,
            TemplateName = "UserInvited",
            IsHtml = true,
            SendImmediately = true,
            MaxRetryCount = 3,
            TemplateVariables = new Dictionary<string, object>
            {
                ["UserName"] = @event.UserName,
                ["AppName"] = appName,
                ["AcceptUrl"] = @event.AcceptUrl,
                ["ExpiresAt"] = @event.ExpiresAt,
                // 链接有效期按天给：邀请是按天算的（默认 7 天），
                // 给分钟数会让邮件里出现「10080 分钟后过期」这种没人读得懂的说法。
                ["ExpiresInDays"] = Math.Max(1, (int)Math.Ceiling((@event.ExpiresAt - DateTime.UtcNow).TotalDays)),
                ["IsResend"] = @event.IsResend,
            },
            Recipients =
            [
                new RecipientInput
                {
                    Address = @event.Email,
                    Name = @event.UserName,
                },
            ],
        };

        await _notificationService.CreateAndSendAsync(request, cancellationToken);

        _logger.LogInformation(
            "Invitation email {Kind} to user {UserId} ({Email}).",
            @event.IsResend ? "resent" : "sent", @event.UserId, @event.Email);
    }

    /// <summary>
    /// 取应用名用于邮件抬头。取不到就用事件里带的站点名，再不行用框架默认值 ——
    /// 一个抬头不完美的邀请，好过发不出去的邀请。
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
            return result.Succeeded && !string.IsNullOrWhiteSpace(result.Data) ? result.Data : "Tnzi";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the application name; falling back to the default.");
            return "Tnzi";
        }
    }
}
