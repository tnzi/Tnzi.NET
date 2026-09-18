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
/// 一封没发出去的邀请是需要被重试、也需要有人知道的。这句对 <c>Result</c> 形态的失败同样成立：
/// <c>CreateAndSendAsync</c> 对业务失败不抛而是返回 Fail，由 <see cref="NotificationDispatch"/> 把它变成异常。
/// </para>
/// <para>
/// ★ 模板是 <c>Templates/Notification/Email/UserInvited.cshtml</c>，随本程序集作为内容文件复制到输出目录。
/// 2026-09-01 加进这个处理器时那份模板没有一起提交：模板存储答 404、通知侧回落到「原始内容」——
/// 而这里没给任何原始内容，于是被邀请人收到一封空主题空正文的信，日志记「Invitation email sent」。
/// 现在两边都守着：模板在（有门禁按处理器源码核对），通知侧对「指定了模板却渲染不出且没有原始内容」返回失败。
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

        EnsureTheLinkCanBeOpened(@event.AcceptUrl);

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

        await NotificationDispatch.SendOrThrowAsync(_notificationService, request, "invitation email", cancellationToken);

        _logger.LogInformation(
            "Invitation email {Kind} to user {UserId} ({Email}).",
            @event.IsResend ? "resent" : "sent", @event.UserId, @event.Email);
    }

    /// <summary>
    /// 接受链接必须是一条邮件客户端打得开的绝对地址，否则拒发。
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>按钮点不开的邮件不能发</strong>，与 <see cref="PasswordResetRequestedEventHandler"/> 同一条线。
    /// 默认的 <c>IInvitationUrlGenerator</c> 在 <c>Identity:Invitation:AcceptUrlTemplate</c> 与
    /// 前端 origin（<c>System:FrontendUrl</c>，旧键 <c>App:FrontendUrl</c>）都没配时返回 <c>/accept-invitation?token=...</c> 这样一条相对路径；
    /// 此前这里原样塞进模板照发：被邀请人收到一封排版漂亮的信，按钮与可复制链接都是相对路径，
    /// 没有任何邮件客户端打得开，而处理器记「sent」、服务层记「issued」，没有任何东西会再试一次。
    /// 这一步在发信之前，抛出去交给事件总线重试不会重复发信。
    /// <para>
    /// 只要求「绝对」不要求 http(s)：移动端深链（<c>acme-app://...</c>）是消费方合法的选择。
    /// 显式排掉 <c>file</c>：Unix 上 <c>Uri</c> 会把 <c>/accept-invitation</c> 解析成绝对的 file URI，
    /// 那正是要拦下的形状。异常消息刻意不带链接本身 —— 令牌明文就在里面，而消息会进日志。
    /// </para>
    /// </remarks>
    private static void EnsureTheLinkCanBeOpened(string acceptUrl)
    {
        var canBeOpened = Uri.TryCreate(acceptUrl, UriKind.Absolute, out var uri) && !uri.IsFile;
        if (canBeOpened)
        {
            return;
        }

        throw new TnziException(
            "Invitation email cannot be sent: the accept link is not an absolute URL, so no mail client could open it. "
            + $"Set Identity:Invitation:AcceptUrlTemplate or {FrontendUrlResolver.KeysForMessages}, or register your own IInvitationUrlGenerator.");
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
