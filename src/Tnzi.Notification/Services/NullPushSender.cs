namespace Tnzi.Notification.Services;

/// <summary>
/// 默认空实现，方便测试
/// </summary>
public class NullPushSender : IPushSender
{
    private readonly ILogger<NullPushSender> _logger;

    public NullPushSender(ILogger<NullPushSender> logger) => _logger = Check.NotNull(logger);

    public Task<SendResult> SendToAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NullPushSender: Sending Push to {DeviceToken}: {Title}", deviceToken, title);
        return Task.FromResult(SendResult.CreateSuccess("null-sender-mock-id"));
    }

    /// <summary>
    /// 主题广播的空实现：与 <see cref="SendToAsync"/> 同一语义（记日志、报成功）。
    /// </summary>
    /// <remarks>
    /// 必须显式实现。<see cref="IPushSender.SendToTopicAsync"/> 的默认实现<b>返回失败</b>，
    /// 沿用它会让「没配推送」这一档在按主题投递时报错，而按设备令牌投递时报成功 ——
    /// 同一个部署对两条路径给出相反的答案。这里的分档判据是<b>意图</b>（这个部署本来就不发推送），
    /// 与寻址方式无关。
    /// </remarks>
    public Task<SendResult> SendToTopicAsync(string topic, string title, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NullPushSender: Sending Push to topic {Topic}: {Title}", topic, title);
        return Task.FromResult(SendResult.CreateSuccess("null-sender-mock-id"));
    }

}


