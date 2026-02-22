namespace Tnzi.Identity.Entities;

/// <summary>
/// Tnzi 用户实体 (扩展自 ASP.NET Core Identity)
/// 只保留核心身份认证字段，个人资料信息存储在 UserDetail 表
/// </summary>
[Table("User")]
public class User : IdentityUser<Guid>, IEntity<Guid>, ISoftDelete, IHasCreationTime, IHasModificationTime
{
    /// <summary>
    /// 获取或设置 组织ID
    /// </summary>
    public Guid? OrganizationId { get; set; }
    
    /// <summary>
    /// 获取或设置 组织
    /// </summary>
    public virtual Organization? Organization { get; set; }
    
    // 实现接口属性
    public bool IsDeleted { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
    
    public object[] GetKeys() => new object[] { Id };
}


