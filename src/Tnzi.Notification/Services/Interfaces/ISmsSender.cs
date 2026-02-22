namespace Tnzi.Notification.Services;

/// <summary>
/// 短信发送服务接口
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// 发送短信到单个接收者
    /// </summary>
    Task<SendResult> SendToAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);

}

