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
    /// 发送推送通知到单个设备，可带点击目标（<see cref="PushContent.Link"/>）与机读数据（<see cref="PushContent.Data"/>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>默认实现只在内容等价于三参数发法时转交过去，否则直接返回失败。</b>只带标题与正文的
    /// <see cref="PushContent"/> 交给 <see cref="SendToAsync(string, string, string, CancellationToken)"/>，
    /// 只实现了那个方法的历史实现因此照常发得出去；带了点击目标或数据时，转交就会把它们安静地丢掉：
    /// 推送照样送达、结果照样成功，而用户点开的是首页、原生客户端拿不到据以路由的那几个键。
    /// </para>
    /// <para>
    /// 这与 <see cref="IEmailSender.SendToWithHeadersAsync"/> 的「丢掉信头照发」刻意相反：那些信头是框架
    /// 自己给每封群发信加的（调用方没要过），少一个可以降级；这里的点击目标与数据是<b>调用方明确要的</b>，
    /// 丢掉它们等于替调用方改了这条推送说的话。要支持它们，重写本方法即可。
    /// </para>
    /// <para>
    /// <b>点击目标与数据是外部输入</b>（消费方通常从路由与业务 id 拼出来），实现方必须把不可投递的值变成
    /// 一个读得懂的 <see cref="SendResult"/> 失败，而不是让底层 SDK 抛原始异常。内置的
    /// <c>Tnzi.Notification.Push</c> 按 FCM 的规则校验：链接必须是绝对的 https 地址，数据不得使用
    /// FCM 与 Android 客户端保留的键。
    /// </para>
    /// </remarks>
    /// <param name="deviceToken">设备令牌。</param>
    /// <param name="content">推送内容。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<SendResult> SendToAsync(string deviceToken, PushContent content, CancellationToken cancellationToken = default)
    {
        Check.NotNull(content);

        return content.HasLinkOrData
            ? Task.FromResult(SendResult.CreateFailure(
                $"{GetType().Name} does not support push click targets or data. "
                + "Implement IPushSender.SendToAsync(string, PushContent, CancellationToken) to deliver PushContent.Link and PushContent.Data, "
                + "or load the Tnzi.Notification.Push module."))
            : SendToAsync(deviceToken, content.Title, content.Body, cancellationToken);
    }

    /// <summary>
    /// 把一条推送广播给某个<b>主题</b>的订阅者（客户端自行 <c>subscribeToTopic</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>与 <see cref="SendToAsync(string, string, string, CancellationToken)"/> 的区别不是粒度，是后端存不存设备标识符。</b>
    /// 按设备令牌投递要求后端持有一张「哪些设备装了这个 App」的持久标识符表；按主题投递
    /// 则一个设备标识符都不存 —— 订阅关系在客户端，后端只说「发给这个主题」。对于「装了
    /// 这个 App」本身就敏感的应用，这是选主题投递的<b>全部理由</b>。
    /// </para>
    /// <para>
    /// ★ <b>默认实现直接返回失败</b>，既不退化成「按令牌逐台发」（没有令牌可发），
    /// 也不报成功。同一条取舍见 <see cref="IEmailSender.SendAsync"/>：一个报了成功却什么都没
    /// 广播出去的实现毫无症状 —— 日志干净、状态是 Sent、没有退信可查。只实现了
    /// <see cref="SendToAsync(string, string, string, CancellationToken)"/> 的历史实现因此仍然满足本接口，行为不变。
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

    /// <summary>
    /// 把一条可带点击目标与机读数据的推送广播给某个<b>主题</b>的订阅者。
    /// </summary>
    /// <remarks>
    /// 默认实现与 <see cref="SendToAsync(string, PushContent, CancellationToken)"/> 同一条取舍：
    /// 只带标题与正文时转交给 <see cref="SendToTopicAsync(string, string, string, CancellationToken)"/>
    /// （它自己支不支持主题投递，答案照旧），带了点击目标或数据时直接返回失败，不丢掉它们照发。
    /// 主题名与内容的校验要求同那两个方法。
    /// </remarks>
    /// <param name="topic">主题名。</param>
    /// <param name="content">推送内容。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<SendResult> SendToTopicAsync(string topic, PushContent content, CancellationToken cancellationToken = default)
    {
        Check.NotNull(content);

        return content.HasLinkOrData
            ? Task.FromResult(SendResult.CreateFailure(
                $"{GetType().Name} does not support push click targets or data. "
                + "Implement IPushSender.SendToTopicAsync(string, PushContent, CancellationToken) to deliver PushContent.Link and PushContent.Data, "
                + "or load the Tnzi.Notification.Push module."))
            : SendToTopicAsync(topic, content.Title, content.Body, cancellationToken);
    }
}
