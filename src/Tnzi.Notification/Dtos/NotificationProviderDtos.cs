namespace Tnzi.Notification.Dtos;

/// <summary>
/// 交给 <see cref="Services.INotificationProviderSelector"/> 的消息快照：选服务商时能看到的一切。
/// </summary>
/// <remarks>
/// 字段都是<b>创建那一刻已经定下来</b>的：渲染后的分类、事务性标志、模板名、收件人。
/// 刻意不给实体 <c>Message</c> 本身 —— 选择器是消费方写的，给它一个可变的实体等于邀请它顺手改点别的。
/// 当前租户与当前用户不在这里：消费方需要时自己注入 <c>ICurrentTenant</c> / <c>ICurrentUser</c>。
/// </remarks>
public sealed class NotificationProviderSelectionContext
{
    /// <summary>渠道。</summary>
    public NotificationType Type { get; init; }

    /// <summary>渲染后的分类（调用方给的，或按模板推出来的）。</summary>
    public string Category { get; init; } = "General";

    /// <summary>事务性消息（密码重置、验证码、账单）还是商业消息。</summary>
    public bool IsTransactional { get; init; }

    /// <summary>优先级。</summary>
    public NotificationPriority Priority { get; init; }

    /// <summary>用了模板时的模板名。</summary>
    public string? TemplateName { get; init; }

    /// <summary>发送者（业务上的发起人）。</summary>
    public Guid? SenderId { get; init; }

    /// <summary>收件人。按消息选择时通常用不上，但「整批都是某国号码」这类判断需要它。</summary>
    public IReadOnlyList<RecipientInput> Recipients { get; init; } = [];
}
