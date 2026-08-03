namespace Tnzi.Storage.Entities;

/// <summary>
/// 文件分享记录
/// </summary>
public class FileShare : EntityBase<Guid>, IHasCreationTime, IHasCreator, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 文件ID
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// 分享令牌（用于生成分享链接）
    /// </summary>
    public string ShareToken { get; set; } = string.Empty;

    /// <summary>
    /// 过期时间（null表示永不过期）
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 访问次数
    /// </summary>
    public int AccessCount { get; set; }

    /// <summary>
    /// 最大访问次数（null表示无限制）
    /// </summary>
    public int? MaxAccessCount { get; set; }

    /// <summary>
    /// 是否需要密码
    /// </summary>
    public bool RequirePassword { get; set; }

    /// <summary>
    /// 密码哈希（如果需要密码）
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 连续输错口令的次数。校验通过即清零；超过
    /// <c>Storage:Share:MaxFailedPasswordAttempts</c> 时该链接自动停用。
    ///
    /// 令牌是 256 位随机数猜不到,但**口令**可以在线爆破 —— 这一列就是那道闸。
    /// </summary>
    public int FailedAttemptCount { get; set; }

    /// <summary>
    /// 最近一次成功取用的时间(null = 从未被用过)。
    ///
    /// 只留一个时间戳而不是一张访问明细表:请求级的"谁在什么时候访问了什么"是
    /// System 模块访问日志的职责,在这里再造一套只会有两份互相矛盾的记录。
    /// 这一列回答的是分享列表最常被问的那个问题 —— "这条链接到底有没有人用过"。
    /// </summary>
    public DateTime? LastAccessedAt { get; set; }

    // Audit info
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
}
