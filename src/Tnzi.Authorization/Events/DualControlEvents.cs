namespace Tnzi.Authorization.Events;

/// <summary>
/// 有人发起了一个需要第二个人点头的动作。
/// </summary>
/// <remarks>
/// 接住它去通知有资格批准的人。<b>框架刻意不内置送达</b>：走站内信、邮件还是值班电话，
/// 取决于这个动作有多急，而框架不知道。
/// </remarks>
public class DualControlRequestedEvent : EventBase
{
    /// <summary>请求标识。</summary>
    public Guid RequestId { get; set; }

    /// <summary>业务动作标识。</summary>
    public string Operation { get; set; } = null!;

    /// <summary>动作目标的标识。</summary>
    public string? TargetId { get; set; }

    /// <summary>发起人。</summary>
    public Guid RequesterId { get; set; }

    /// <summary>许可的失效时间。</summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// 一个双人授权请求被批准或拒绝了。
/// </summary>
public class DualControlDecidedEvent : EventBase
{
    /// <summary>请求标识。</summary>
    public Guid RequestId { get; set; }

    /// <summary>业务动作标识。</summary>
    public string Operation { get; set; } = null!;

    /// <summary>动作目标的标识。</summary>
    public string? TargetId { get; set; }

    /// <summary>发起人。</summary>
    public Guid RequesterId { get; set; }

    /// <summary>做出决定的人。</summary>
    public Guid ApproverId { get; set; }

    /// <summary>是批准还是拒绝。</summary>
    public bool Approved { get; set; }
}
