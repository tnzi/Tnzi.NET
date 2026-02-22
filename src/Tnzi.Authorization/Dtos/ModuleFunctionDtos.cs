namespace Tnzi.Authorization.Dtos;

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
