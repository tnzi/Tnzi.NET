namespace Tnzi.Notification.Services;

/// <summary>
/// 部署方显然要发推送、而没带键的消息却没有可用的默认发送器时的回退：每次调用都失败，并指名怎么修。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="NullPushSender"/> 的分工是按<b>意图</b>划的，不是按有没有实现划的：
/// </para>
/// <list type="bullet">
///   <item>什么都没配 —— 这个部署本来就不发推送，
///     沿用 <see cref="NullPushSender"/>（记一条日志、报成功），行为与拆分前一致；</item>
///   <item><b>配了</b> <c>Notification:PushSender</c> 却没有实现 —— 部署方明确要发推送，
///     只是没加载 <c>Tnzi.Notification.Push</c>（<see cref="MissingImplementationGuidance"/>）。
///     此时报成功就是给每一条没发出去的推送开一张已投递的证明，而这种谎没有任何症状：
///     日志干净、状态是 Sent、没有退信可查；</item>
///   <item>只配了具名节 <c>Notification:PushSenders</c>、没配默认节 —— 同样显然要推送，
///     没带 <c>ProviderKey</c> 的消息落进一个报成功的空实现是路由错误不是开发模式
///     （<see cref="MissingDefaultGuidance"/>）。与邮件 / 短信的 <c>Unconfigured*Sender</c> 同一分档；
///     这一档不分「有没有加载子模块」，两边都是它。</item>
/// </list>
/// <para>
/// 同一条取舍见 <see cref="UnconfiguredFaxSender"/>。名字直说条件（<c>Unconfigured</c>），
/// 免得有人按 <c>Null*</c> 的类比推断它的行为。
/// </para>
/// </remarks>
public class UnconfiguredPushSender : IPushSender
{
    /// <summary>配了推送却没有实现时的提示：要装的是包。</summary>
    internal const string MissingImplementationGuidance =
        "Notification:PushSender is configured but no IPushSender implementation is registered. "
        + "Load the Tnzi.Notification.Push module ([DependsOn(typeof(NotificationPushModule))]) "
        + "or register your own IPushSender.";

    /// <summary>只配了具名节没配默认节时的提示：要改的是配置或路由，不是包。</summary>
    internal const string MissingDefaultGuidance =
        "Named push senders are configured (Notification:PushSenders) but no default push sender is. "
        + "Messages without a ProviderKey have nowhere to go: configure Notification:PushSender, "
        + "register a default IPushSender, register an INotificationProviderSelector, "
        + "or set ProviderKey on every push notification.";

    /// <summary>
    /// 缺失实现时给出的提示，同时用于日志与 <see cref="SendResult"/> 的失败原因。
    /// 保留这个名字是为了既有引用；语义等同 <see cref="MissingImplementationGuidance"/>。
    /// </summary>
    internal const string Guidance = MissingImplementationGuidance;

    private readonly ILogger<UnconfiguredPushSender> _logger;
    private readonly string _guidance;

    /// <summary>初始化一个指名「要加载哪个包」的 <see cref="UnconfiguredPushSender"/> 实例。</summary>
    public UnconfiguredPushSender(ILogger<UnconfiguredPushSender> logger)
        : this(logger, MissingImplementationGuidance)
    {
    }

    /// <summary>初始化一个带指定提示的 <see cref="UnconfiguredPushSender"/> 实例。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="guidance">失败原因里要说的话（<see cref="MissingImplementationGuidance"/> 或 <see cref="MissingDefaultGuidance"/>）。</param>
    public UnconfiguredPushSender(ILogger<UnconfiguredPushSender> logger, string guidance)
    {
        _logger = Check.NotNull(logger);
        _guidance = Check.NotNullOrWhiteSpace(guidance);
    }

    /// <summary>只配了具名节没配默认节时用的实例。</summary>
    public static UnconfiguredPushSender ForMissingDefault(ILogger<UnconfiguredPushSender> logger)
        => new(logger, MissingDefaultGuidance);

    /// <inheritdoc />
    public Task<SendResult> SendToAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogError("Push delivery to {DeviceToken} refused: {Guidance}", deviceToken, _guidance);
        return Task.FromResult(SendResult.CreateFailure(_guidance));
    }

    /// <inheritdoc />
    /// <remarks>
    /// 显式实现而不是沿用 <see cref="IPushSender.SendToTopicAsync"/> 的默认失败：那条默认体报的是
    /// 「这个实现不支持主题投递」，会把部署方引向「换一个支持主题的实现」；而这里真正的原因是
    /// <b>包没加载</b>，要说的是加载哪个包。两句话都失败，但只有一句能让人把它修好。
    /// </remarks>
    public Task<SendResult> SendToTopicAsync(string topic, string title, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogError("Push delivery to topic {Topic} refused: {Guidance}", topic, _guidance);
        return Task.FromResult(SendResult.CreateFailure(_guidance));
    }
}
