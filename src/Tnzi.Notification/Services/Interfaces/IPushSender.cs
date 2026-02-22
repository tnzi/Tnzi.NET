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

}

