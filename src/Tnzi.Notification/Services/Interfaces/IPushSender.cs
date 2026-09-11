namespace Tnzi.Notification.Services;

/// <summary>
/// 推送通知服务接口
/// </summary>
public interface IPushSender
{
    /// <summary>
    /// 发送推送通知到单个设备
    /// </summary>
    Task<SendResult> SendToAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// 把一条推送广播给某个<b>主题</b>的订阅者（客户端自行 <c>subscribeToTopic</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>与 <see cref="SendToAsync"/> 的区别不是粒度，是后端存不存设备标识符。</b>
    /// 按设备令牌投递要求后端持有一张「哪些设备装了这个 App」的持久标识符表；按主题投递
    /// 则一个设备标识符都不存 —— 订阅关系在客户端，后端只说「发给这个主题」。对于「装了
    /// 这个 App」本身就敏感的应用，这是选主题投递的<b>全部理由</b>。
    /// </para>
    /// <para>
    /// ★ <b>默认实现直接返回失败</b>，既不退化成「按令牌逐台发」（没有令牌可发），
    /// 也不报成功。同一条取舍见 <see cref="IEmailSender.SendAsync"/>：一个报了成功却什么都没
    /// 广播出去的实现毫无症状 —— 日志干净、状态是 Sent、没有退信可查。只实现了
    /// <see cref="SendToAsync"/> 的历史实现因此仍然满足本接口，行为不变。
    /// </para>
    /// <para>
    /// <b>主题名是外部输入</b>（消费方通常从配置里拼），实现方必须把不可投递的名字变成一个
    /// 读得懂的 <see cref="SendResult"/> 失败，而不是让底层 SDK 抛原始异常。内置的
    /// <c>Tnzi.Notification.Push</c> 按 FCM 的字符集规则 <c>[a-zA-Z0-9-_.~%]+</c> 校验。
    /// </para>
    /// </remarks>
    /// <param name="topic">主题名。</param>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<SendResult> SendToTopicAsync(string topic, string title, string body, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SendResult.CreateFailure(
            $"{GetType().Name} does not support topic push delivery. "
            + "Implement IPushSender.SendToTopicAsync to broadcast to a topic's subscribers, "
            + "or load the Tnzi.Notification.Push module."));
    }
}
