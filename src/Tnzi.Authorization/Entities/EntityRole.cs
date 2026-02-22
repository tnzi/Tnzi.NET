namespace Tnzi.Authorization.Entities;

/// <summary>
/// 实体角色实体（用于数据授权）
/// </summary>
public class EntityRole : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 实体信息ID
    /// </summary>
    public Guid EntityInfoId { get; set; }

    /// <summary>
    /// 获取或设置 实体信息
    /// </summary>
    public virtual EntityInfo EntityInfo { get; set; } = null!;

    /// <summary>
    /// 获取或设置 角色ID
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// 获取或设置 数据权限操作类型（查询、更新、删除等）
    /// </summary>
    public DataAuthOperation Operation { get; set; }

    /// <summary>
    /// 获取或设置 过滤条件（JSON格式的查询条件）
    /// </summary>
    public string? Filter { get; set; }

    /// <summary>
    /// 获取或设置 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 数据权限操作类型
/// </summary>
public enum DataAuthOperation
{
    /// <summary>
    /// 查询
    /// </summary>
    Query = 1,

    /// <summary>
    /// 更新
    /// </summary>
    Update = 2,

    /// <summary>
    /// 删除
    /// </summary>
    Delete = 4,

    /// <summary>
    /// 全部操作
    /// </summary>
    All = Query | Update | Delete
}

