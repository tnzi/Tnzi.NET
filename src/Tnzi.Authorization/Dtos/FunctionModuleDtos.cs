namespace Tnzi.Authorization.Dtos;

/// <summary>
/// 创建功能模块请求
/// </summary>
public class CreateFunctionModuleRequest
{
    /// <summary>
    /// 模块名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 模块代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 父模块ID
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 排序号
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// 更新功能模块请求
/// </summary>
public class UpdateFunctionModuleRequest
{
    /// <summary>
    /// 模块名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 模块代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 父模块ID
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 排序号
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }
}
