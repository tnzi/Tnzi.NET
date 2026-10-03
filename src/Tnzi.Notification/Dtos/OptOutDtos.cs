namespace Tnzi.Notification.Dtos;

/// <summary>
/// 一条退订记录的管理端读取形态。
/// </summary>
/// <remarks>
/// 地址原样给出而不掩码：这一面是给持 <c>notification.optOut.view</c> 的操作者回答合规问询用的
/// （「这个地址何时经哪个渠道退订」「客户来电说误点了请恢复」），掩码了就答不了；
/// 面向收件人的匿名落地页（<see cref="UnsubscribePreviewDto"/>）才掩码，那一页任何拿到链接的人都打得开。
/// </remarks>
public class OptOutDto
{
    /// <summary>记录 ID</summary>
    public Guid Id { get; set; }

    /// <summary>退订的地址（已归一化：去空白 + 小写，传真为纯数字）</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>渠道</summary>
    public NotificationType Channel { get; set; }

    /// <summary>通知分类；<c>null</c> 表示该渠道全部退订</summary>
    public string? Category { get; set; }

    /// <summary>来源（"one-click link" / "one-click header" / "admin:{userId}" / 消费方自定）</summary>
    public string? Source { get; set; }

    /// <summary>退订原因</summary>
    public string? Reason { get; set; }

    /// <summary>退订时间</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 退订名单的分页查询。
/// </summary>
public class OptOutQueryDto : PagedQueryDto
{
    /// <summary>
    /// 按地址筛选：包含匹配，按归一化后的写法比对，不区分大小写。同时给了 <see cref="Channel"/> 时先走该渠道登记时的
    /// 同一条归一化（传真号码任一写法都命中存成纯数字的那一行）。
    /// </summary>
    public string? Address { get; set; }

    /// <summary>按渠道筛选</summary>
    public NotificationType? Channel { get; set; }

    /// <summary>按分类精确筛选（空 = 不筛）</summary>
    public string? Category { get; set; }

    /// <summary>
    /// 给了 <see cref="Category"/> 时，同时带上整渠道退订的行（<c>Category IS NULL</c>）。
    /// </summary>
    /// <remarks>
    /// 整渠道退订覆盖该渠道下的任何分类，所以一张只看某个分类的抑制名单少了这些行，就解释不了
    /// 「这个人为什么收不到」：他退订的是整个渠道，而这一页看不见。不给分类时本来就不筛分类，
    /// 这个开关不起作用；通常与 <see cref="Channel"/> 一起给，否则带进来的是每个渠道的整渠道行。
    /// 默认 <c>false</c>，既有调用方的精确匹配一字不变。
    /// </remarks>
    public bool IncludeChannelWide { get; set; }

    /// <summary>退订时间下界（含）</summary>
    public DateTime? From { get; set; }

    /// <summary>退订时间上界（含）</summary>
    public DateTime? To { get; set; }
}

/// <summary>
/// 管理端手工登记一条退订（服务商投诉转来、客户来电、导入的黑名单）。
/// </summary>
public class CreateOptOutDto
{
    /// <summary>邮箱 / 手机号 / 传真号</summary>
    [Required]
    [StringLength(500)]
    public string Address { get; set; } = null!;

    /// <summary>渠道</summary>
    public NotificationType Channel { get; set; }

    /// <summary>通知分类；留空 = 该渠道全部退订</summary>
    [StringLength(100)]
    public string? Category { get; set; }

    /// <summary>原因（如 "provider complaint" / 客户来电要点）</summary>
    [StringLength(1000)]
    public string? Reason { get; set; }
}
