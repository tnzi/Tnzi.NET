namespace Tnzi.Notification.Services;

/// <summary>
/// 配置了推送渠道、但没有任何实现被注册时的回退：每次调用都失败，并指名要加载哪个包。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="NullPushSender"/> 的分工是按<b>意图</b>划的，不是按有没有实现划的：
/// </para>
/// <list type="bullet">
///   <item>没配 <c>Notification:PushSender</c> —— 这个部署本来就不发推送，
///     沿用 <see cref="NullPushSender"/>（记一条日志、报成功），行为与拆分前一致；</item>
///   <item><b>配了</b> <c>Notification:PushSender</c> 却没有实现 —— 部署方明确要发推送，
///     只是没加载 <c>Tnzi.Notification.Push</c>。此时报成功就是给每一条没发出去的推送
///     开一张已投递的证明，而这种谎没有任何症状：日志干净、状态是 Sent、没有退信可查。</item>
/// </list>
/// <para>
/// 同一条取舍见 <see cref="UnconfiguredFaxSender"/>。名字直说条件（<c>Unconfigured</c>），
/// 免得有人按 <c>Null*</c> 的类比推断它的行为。
/// </para>
/// </remarks>
public class UnconfiguredPushSender : IPushSender
{
    /// <summary>缺失实现时给出的提示，同时用于日志与 <see cref="SendResult"/> 的失败原因。</summary>
    internal const string Guidance =
        "Notification:PushSender is configured but no IPushSender implementation is registered. "
        + "Load the Tnzi.Notification.Push module ([DependsOn(typeof(NotificationPushModule))]) "
        + "or register your own IPushSender.";

    private readonly ILogger<UnconfiguredPushSender> _logger;

    /// <summary>初始化一个 <see cref="UnconfiguredPushSender"/> 实例。</summary>
    public UnconfiguredPushSender(ILogger<UnconfiguredPushSender> logger) => _logger = Check.NotNull(logger);

    /// <inheritdoc />
    public Task<SendResult> SendToAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogError("Push delivery to {DeviceToken} refused: {Guidance}", deviceToken, Guidance);
        return Task.FromResult(SendResult.CreateFailure(Guidance));
    }

    /// <inheritdoc />
    /// <remarks>
    /// 显式实现而不是沿用 <see cref="IPushSender.SendToTopicAsync"/> 的默认失败：那条默认体报的是
    /// 「这个实现不支持主题投递」，会把部署方引向「换一个支持主题的实现」；而这里真正的原因是
    /// <b>包没加载</b>，要说的是加载哪个包。两句话都失败，但只有一句能让人把它修好。
    /// </remarks>
    public Task<SendResult> SendToTopicAsync(string topic, string title, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogError("Push delivery to topic {Topic} refused: {Guidance}", topic, Guidance);
        return Task.FromResult(SendResult.CreateFailure(Guidance));
    }
}
