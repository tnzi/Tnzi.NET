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

    // Audit info
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
}
