namespace Tnzi.Notification.Services;

/// <summary>
/// 消费方的路由策略：一条<b>没带</b> <c>ProviderKey</c> 的消息该由哪个服务商投递。
/// </summary>
/// <remarks>
/// <para>
/// 框架只提供机制（具名发送器 + 落库的键 + 按键派发），「验证码走 A、营销走 B」这种规则是业务，
/// 由消费方实现本接口注册进来。默认实现恒返回 <see langword="null"/>（= 默认发送器）。
/// </para>
/// <para>
/// ★ <b>在创建那一刻调用，结果写进 <c>Message.ProviderKey</c></b>，而不是每次派发时现算：
/// 排队、定时、重试、重启续发都是从落库的那条记录出发，同一条消息的每一次投递尝试必须走同一家 ——
/// 否则重试换了一家服务商，投递报告上写的与真正发出去的对不上。规则改了只影响之后创建的消息，
/// 这与「已经排期的消息按当时的模板快照发」同一取舍。
/// </para>
/// <para>
/// ★ 框架自己发出的消息（密码重置、二次验证码、邀请、账单）也经这里 —— 它们不带键，
/// 消费方靠 <see cref="NotificationProviderSelectionContext.IsTransactional"/> 与
/// <see cref="NotificationProviderSelectionContext.Category"/> 就能把它们路由到指定的那家。
/// </para>
/// <para>
/// 返回的键必须是已注册的：创建路径会校验，未注册的键让创建失败（500，原因指名是选择器给的），
/// 绝不静默退回默认发送器。
/// </para>
/// </remarks>
public interface INotificationProviderSelector
{
    /// <summary>
    /// 为 <paramref name="context"/> 描述的消息选一个服务商键；返回 <see langword="null"/> 或 <c>default</c> = 默认发送器。
    /// </summary>
    ValueTask<string?> SelectAsync(NotificationProviderSelectionContext context, CancellationToken cancellationToken = default);
}
