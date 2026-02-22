namespace Tnzi.Authorization.Dtos;

/// <summary>
/// 创建实体信息请求
/// </summary>
public class CreateEntityInfoRequest
{
    /// <summary>
    /// 实体名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 实体类型名称（完整类型名）
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// 实体显示名称
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 是否启用数据授权
    /// </summary>
    public bool IsDataAuthEnabled { get; set; } = true;
}

/// <summary>
/// 更新实体信息请求
/// </summary>
public class UpdateEntityInfoRequest
{
    /// <summary>
    /// 实体名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 实体显示名称
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 是否启用数据授权
    /// </summary>
    public bool IsDataAuthEnabled { get; set; } = true;
}

/// <summary>
/// 创建实体角色请求
/// </summary>
public class CreateEntityRoleRequest
{
    /// <summary>
    /// 实体信息ID
    /// </summary>
    public Guid EntityInfoId { get; set; }

    /// <summary>
    /// 角色ID
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// 数据权限操作类型
    /// </summary>
    public Entities.DataAuthOperation Operation { get; set; }

    /// <summary>
    /// 过滤条件（JSON格式）
    /// </summary>
    public string? Filter { get; set; }
}

/// <summary>
/// 更新实体角色请求
/// </summary>
public class UpdateEntityRoleRequest
{
    /// <summary>
    /// 数据权限操作类型
    /// </summary>
    public Entities.DataAuthOperation Operation { get; set; }

    /// <summary>
    /// 过滤条件（JSON格式）
    /// </summary>
    public string? Filter { get; set; }
}

/// <summary>
/// 批量创建实体角色请求
/// </summary>
public class BatchEntityRoleRequest
{
    /// <summary>
    /// 实体信息ID
    /// </summary>
    public Guid EntityInfoId { get; set; }

    /// <summary>
    /// 角色ID列表
    /// </summary>
    public List<Guid> RoleIds { get; set; } = new();

    /// <summary>
    /// 数据权限操作类型
    /// </summary>
    public Entities.DataAuthOperation Operation { get; set; }

    /// <summary>
    /// 过滤条件（JSON格式）
    /// </summary>
    public string? Filter { get; set; }
}
