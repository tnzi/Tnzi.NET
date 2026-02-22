namespace Tnzi.Authorization.Entities;

/// <summary>
/// 实体信息实体（用于数据授权）
/// </summary>
public class EntityInfo : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 实体名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 实体类型名称（完整类型名）
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 实体显示名称
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 获取或设置 是否启用数据授权
    /// </summary>
    public bool IsDataAuthEnabled { get; set; } = true;

    /// <summary>
    /// 获取或设置 实体角色集合
    /// </summary>
    public virtual ICollection<EntityRole> EntityRoles { get; set; } = new List<EntityRole>();
}

