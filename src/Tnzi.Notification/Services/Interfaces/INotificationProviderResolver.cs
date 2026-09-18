namespace Tnzi.Notification.Services;

/// <summary>
/// 按服务商键取一条渠道的发送器。
/// </summary>
/// <remarks>
/// <para>
/// 一条渠道可以有多个发送器：<b>默认</b>的是普通 DI 注册（注入 <see cref="IEmailSender"/> 拿到的那个），
/// <b>具名</b>的是同一接口的 keyed service（键 = 服务商键）。<c>Notification:MailSenders:{key}</c>
/// 这类配置节由模块在启动时注册成 keyed service；消费方在代码里
/// <c>AddKeyedScoped&lt;IEmailSender&gt;("sendgrid", …)</c> 也一样能被这里取到 —— 两条来路一个出口。
/// </para>
/// <para>
/// ★ <b>取不到就返回 <see langword="null"/>，绝不退回默认发送器。</b>调用方指定了一个键，
/// 说明那家服务商对这条消息是有意义的（验证码短码、指定发件域、合规要求的那家）；
/// 换一家发出去，外观是「发送成功」，而那正是这个模块反复吃过亏的失效形态。
/// </para>
/// </remarks>
public interface INotificationProviderResolver
{
    /// <summary>
    /// 取 <typeparamref name="TSender"/> 渠道上键为 <paramref name="providerKey"/> 的发送器；
    /// 键为 <see langword="null"/> / 空白 / <c>default</c> 时取默认发送器。取不到返回 <see langword="null"/>。
    /// </summary>
    /// <typeparam name="TSender"><see cref="IEmailSender"/> / <see cref="ISmsSender"/> / <see cref="IPushSender"/> / <see cref="IFaxSender"/> 之一。</typeparam>
    TSender? Resolve<TSender>(string? providerKey) where TSender : class;

    /// <summary>
    /// <paramref name="type"/> 渠道上是否注册了键为 <paramref name="providerKey"/> 的发送器。
    /// 创建消息时用它把「键写错了」拦在 400 上，而不是等到后台派发才失败。
    /// </summary>
    bool IsRegistered(NotificationType type, string? providerKey);
}
