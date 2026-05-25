namespace Tnzi.Authorization.Dtos;

/// <summary>
/// Module function read DTO returned by admin list / detail endpoints.
/// Excludes audit and navigation fields that should not leak to the API surface.
/// </summary>
public class ModuleFunctionDto
{
    /// <summary>
    /// Function ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Function name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Function code (permission name)
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Function description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Owning module ID
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// Whether the function is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Display order within the owning module
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// True when the function row is owned by an
    /// <c>IPermissionDefinitionProvider</c>. Admin UI shows a "system"
    /// badge and locks the Code / Name / ModuleId edit fields. Admin can
    /// still toggle IsEnabled for emergency disable without redeploying.
    /// </summary>
    public bool IsSystemManaged { get; set; }
}

/// <summary>
/// 创建功能请求
/// </summary>
public class CreateModuleFunctionRequest
{
    /// <summary>
    /// 功能名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 功能代码（权限名称）
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 所属模块ID
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// 功能描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 排序号
    /// </summary>
    public int Order { get; set; }
}

/// <summary>
/// 更新功能请求
/// </summary>
public class UpdateModuleFunctionRequest
{
    /// <summary>
    /// 功能名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 功能代码（权限名称）
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 所属模块ID
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// 功能描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 排序号
    /// </summary>
    public int Order { get; set; }
}
