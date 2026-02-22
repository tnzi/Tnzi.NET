namespace Tnzi.Identity.Entities;

/// <summary>
/// 密码历史实体
/// </summary>
public class PasswordHistory : EntityBase<Guid>, IHasCreationTime
{
    /// <summary>
    /// 获取或设置 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 获取或设置 用户
    /// </summary>
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// 获取或设置 密码哈希值
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}

