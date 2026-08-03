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

// 统计趋势时间间隔已收口到核心 Tnzi.Utilities.TrendInterval（成员名与序号完全一致，
// 线缆形态不变——框架在 AspNetCore 全局注册 JsonStringEnumConverter，仍序列化为
// "Daily"/"Weekly"/"Monthly"）。分桶与标签走 Tnzi.Utilities.TimeBucket。

/// <summary>
/// 趋势数据点
/// </summary>
public class TrendDataPoint
{
    /// <summary>
    /// 时间标签 (e.g., "2026-02-28", "2026-W09", "2026-02")
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 时间段起始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 总数量
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 发送成功数量
    /// </summary>
    public int SentCount { get; set; }

    /// <summary>
    /// 发送失败数量
    /// </summary>
    public int FailedCount { get; set; }
}

/// <summary>
/// 通知统计趋势
/// </summary>
public class NotificationTrendDto
{
    /// <summary>
    /// 时间间隔
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TrendInterval Interval { get; set; }

    /// <summary>
    /// 起始时间
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// 数据点列表
    /// </summary>
    public List<TrendDataPoint> DataPoints { get; set; } = new();
}

