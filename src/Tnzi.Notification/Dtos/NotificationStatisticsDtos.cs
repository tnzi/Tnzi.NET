namespace Tnzi.Notification.Dtos;

/// <summary>
/// 通知统计DTO
/// </summary>
public class NotificationStatisticsDto
{
    /// <summary>
    /// 总通知数
    /// </summary>
    public int TotalNotifications { get; set; }

    /// <summary>
    /// 已发送数量
    /// </summary>
    public int SentCount { get; set; }

    /// <summary>
    /// 发送中数量
    /// </summary>
    public int SendingCount { get; set; }

    /// <summary>
    /// 失败数量
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 待发送数量
    /// </summary>
    public int PendingCount { get; set; }

    /// <summary>
    /// 已取消数量
    /// </summary>
    public int CancelledCount { get; set; }

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate { get; set; }
}

/// <summary>
/// 渠道统计DTO
/// </summary>
public class ChannelStatisticsDto
{
    /// <summary>
    /// 渠道类型 (序列化为字符串以确保 API 兼容性)
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotificationType Channel { get; set; }

    /// <summary>
    /// 总数量
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 成功数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 失败数量
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate { get; set; }
}

/// <summary>
/// 状态统计DTO
/// </summary>
public class StatusStatisticsDto
{
    /// <summary>
    /// 状态类型 (序列化为字符串以确保 API 兼容性)
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotificationStatus Status { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 百分比
    /// </summary>
    public double Percentage { get; set; }
}

