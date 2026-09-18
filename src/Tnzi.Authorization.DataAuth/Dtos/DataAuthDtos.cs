namespace Tnzi.Authorization.DataAuth.Dtos;

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
    /// 实体类型名称：CLR 全名（命名空间 + 类型名，<b>不带程序集</b>，即 <c>typeof(T).FullName</c>）。
    /// 过滤路径按它等值查表，AssemblyQualifiedName 永远查不到；解析不到已加载实体的名字登记时 400。
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
