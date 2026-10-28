namespace Tnzi.Notification.Services;

/// <summary>
/// 默认空实现，方便测试
/// </summary>
/// <remarks>
/// 日志里的设备令牌一律打掩码（<see cref="PushTokenMask"/>）：令牌是凭据，开发期的日志同样会进截图与工单。
/// </remarks>
public class NullPushSender : IPushSender
{
    private readonly ILogger<NullPushSender> _logger;

    public NullPushSender(ILogger<NullPushSender> logger) => _logger = Check.NotNull(logger);

    public Task<SendResult> SendToAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NullPushSender: Sending Push to {DeviceToken}: {Title}", PushTokenMask.Of(deviceToken), title);
        return Task.FromResult(SendResult.CreateSuccess("null-sender-mock-id"));
    }

    /// <summary>
    /// 带点击目标与数据的空实现：与三参数那个同一语义（记日志、报成功）。
    /// </summary>
    /// <remarks>
    /// 必须显式实现，理由同 <see cref="SendToTopicAsync(string, string, string, CancellationToken)"/>：接口默认体对带链接或数据的内容
    /// <b>返回失败</b>，沿用它会让「没配推送」这一档对同一条推送按发法给出相反的答案。
    /// 不校验链接与数据：那是 FCM 的规则，住在 <c>Tnzi.Notification.Push</c> 里，这个部署本来就不发推送。
    /// </remarks>
    public Task<SendResult> SendToAsync(string deviceToken, PushContent content, CancellationToken cancellationToken = default)
    {
        Check.NotNull(content);

        _logger.LogInformation("NullPushSender: Sending Push to {DeviceToken}: {Title} (link: {Link}, data keys: {DataKeys})",
            PushTokenMask.Of(deviceToken), content.Title, content.Link, DescribeDataKeys(content));
        return Task.FromResult(SendResult.CreateSuccess("null-sender-mock-id"));
    }

    /// <summary>
    /// 主题广播的空实现：与 <see cref="SendToAsync(string, string, string, CancellationToken)"/> 同一语义（记日志、报成功）。
    /// </summary>
    /// <remarks>
    /// 必须显式实现。<see cref="IPushSender.SendToTopicAsync(string, string, string, CancellationToken)"/> 的默认实现<b>返回失败</b>，
    /// 沿用它会让「没配推送」这一档在按主题投递时报错，而按设备令牌投递时报成功 ——
    /// 同一个部署对两条路径给出相反的答案。这里的分档判据是<b>意图</b>（这个部署本来就不发推送），
    /// 与寻址方式无关。
    /// </remarks>
    public Task<SendResult> SendToTopicAsync(string topic, string title, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NullPushSender: Sending Push to topic {Topic}: {Title}", topic, title);
        return Task.FromResult(SendResult.CreateSuccess("null-sender-mock-id"));
    }

    /// <summary>带点击目标与数据的主题广播空实现：记日志、报成功，理由同上。</summary>
    public Task<SendResult> SendToTopicAsync(string topic, PushContent content, CancellationToken cancellationToken = default)
    {
        Check.NotNull(content);

        _logger.LogInformation("NullPushSender: Sending Push to topic {Topic}: {Title} (link: {Link}, data keys: {DataKeys})",
            topic, content.Title, content.Link, DescribeDataKeys(content));
        return Task.FromResult(SendResult.CreateSuccess("null-sender-mock-id"));
    }

    /// <summary>只记数据的<b>键</b>：值是业务数据，开发期日志也不该带着它们。</summary>
    private static string DescribeDataKeys(PushContent content)
        => content.Data is { Count: > 0 } data ? string.Join(", ", data.Keys) : "none";
}
