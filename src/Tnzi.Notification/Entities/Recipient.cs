
namespace Tnzi.Notification.Entities;

/// <summary>
/// 消息接收者
/// </summary>
public class Recipient : EntityBase<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 消息ID
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 消息实体
    /// </summary>
    public virtual Message Message { get; set; } = null!;

    /// <summary>
    /// 接收者地址（邮箱/手机号/设备Token）
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 接收者名称（可选）
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 发送状态
    /// </summary>
    public NotificationStatus Status { get; set; }

    /// <summary>
    /// 发送时间
    /// </summary>
    public DateTime? SentTime { get; set; }

    /// <summary>
    /// 失败原因
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 外部服务返回的消息ID（用于追踪）
    /// </summary>
    public string? ExternalMessageId { get; set; }

    /// <summary>
    /// 关联系统用户ID（用于站内通知收件箱）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 已读状态
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>
    /// 已读时间
    /// </summary>
    public DateTime? ReadTime { get; set; }

    /// <summary>
    /// 因本人的静默时段而<b>延后到</b>这一刻再投递（UTC 绝对时刻）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ 静默时段的正确语义是<b>延后</b>而不是丢弃：它表达的是「现在别吵我」，
    /// 到点丢掉等于把「晚点再说」执行成「再也不说」。所以被拦下的收件人不是
    /// <see cref="NotificationStatus.Cancelled"/>（那是退订 / 渠道开关 / 每小时上限的处置），
    /// 而是 <see cref="NotificationStatus.Scheduled"/> 加上这个时刻。
    /// </para>
    /// <para>
    /// ★ 存<b>绝对时刻</b>而不是一个 <c>TimeOnly</c>：偏好里那个「06:00」无从判断是今天的
    /// 还是明天的，而恢复扫描需要一个能直接比较的值。
    /// </para>
    /// <para>
    /// 到期后由 <c>NotificationDispatchBackgroundService</c> 接手，
    /// <c>SendAsync</c> 的待发筛选也认这一条。
    /// </para>
    /// </remarks>
    public DateTime? DeferredUntil { get; set; }
}
