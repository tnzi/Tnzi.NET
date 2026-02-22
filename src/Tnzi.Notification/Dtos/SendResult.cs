namespace Tnzi.Notification.Dtos;

/// <summary>
/// 发送结果
/// </summary>
public class SendResult
{
    /// <summary>
    /// 是否发送成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 外部服务返回的消息ID（用于追踪）
    /// </summary>
    public string? ExternalMessageId { get; set; }

    /// <summary>
    /// 失败原因
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static SendResult CreateSuccess(string? externalMessageId = null)
    {
        return new SendResult
        {
            Success = true,
            ExternalMessageId = externalMessageId
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static SendResult CreateFailure(string failureReason)
    {
        return new SendResult
        {
            Success = false,
            FailureReason = failureReason
        };
    }
}

