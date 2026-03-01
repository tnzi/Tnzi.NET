namespace Tnzi.Identity.Entities;

/// <summary>
/// Tnzi 角色实体 (扩展自 ASP.NET Core Identity)
/// </summary>
[Table("Role")]
public class Role : IdentityRole<Guid>, IEntity<Guid>, IHasCreationTime
{
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 是否为系统内置角色（不可删除、不可重命名）
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// 是否为默认角色（新注册用户自动分配）
    /// </summary>
    public bool IsDefault { get; set; }

    public object[] GetKeys() => new object[] { Id };
}
