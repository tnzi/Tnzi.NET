namespace Tnzi.Hosting.Events.Handlers;

/// <summary>
/// 内置邮件处理器共用的发信出口：把 <see cref="INotificationService.CreateAndSendAsync"/> 的
/// <c>Result</c> 形态失败变成异常，交给事件总线。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <strong><c>CreateAndSendAsync</c> 对业务失败从不抛异常</strong>：收件人校验不过、
/// 服务商键解析不出（只配了具名发送器节而没配默认节、请求指了未注册的键）都在落库之前返回 Fail；
/// SMTP 对全部收件人失败时返回 Fail(500)。处理器此前把返回值丢掉、紧接着记一条「... sent」，
/// 于是「不吞异常交总线重试」的承诺对这一整类失败一个字都不成立 ——
/// 欢迎邮件、密码重置、验证码、邀请在这些配置错误下静默丢失，日志里却是「sent」。
/// </para>
/// <para>
/// ★ <strong>重试语义</strong>：创建阶段失败没有落库，总线重试不会重复发信。
/// 发送阶段失败已经留下一条全部收件人 Failed 的消息行（没有任何人收到），总线重试会再建一行 ——
/// 这是可接受的；那条旧行刻意<b>不</b>取消：总线未开启重试的部署（默认）只剩管理端 RetryFailed
/// 这一条恢复路径，取消它等于把唯一的救援也关掉。代价是管理员对旧行 RetryFailed 而总线又重试成功时
/// 可能收到两封，两封总好过零封。
/// </para>
/// </remarks>
internal static class NotificationDispatch
{
    /// <summary>
    /// 创建并立即发送一条通知；<c>Result</c> 失败时抛 <see cref="TnziException"/>。
    /// </summary>
    /// <param name="notificationService">通知服务。</param>
    /// <param name="request">通知请求。</param>
    /// <param name="what">这封信是什么（进异常消息，例如 "welcome email"）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="TnziException">通知服务返回失败。</exception>
    public static async Task SendOrThrowAsync(
        INotificationService notificationService,
        CreateNotificationRequest request,
        string what,
        CancellationToken cancellationToken)
    {
        Check.NotNull(notificationService);
        Check.NotNull(request);
        Check.NotNullOrWhiteSpace(what);

        var result = await notificationService.CreateAndSendAsync(request, cancellationToken);
        if (result.Succeeded)
        {
            return;
        }

        throw new TnziException(
            result.ErrorCode ?? ErrorCodes.NOTIFICATION_ERROR,
            $"Failed to send the {what}: {result.Message ?? "the notification service returned a failure without a message"}");
    }
}
