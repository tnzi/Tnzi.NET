namespace Tnzi.AI.Tools.Models;

/// <summary>
/// 工具定义
/// </summary>
public class ToolDefinition
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 工具描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 工具组名称
    /// </summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// 工具提供者类型
    /// </summary>
    public Type ProviderType { get; set; } = null!;

    /// <summary>
    /// 方法信息
    /// </summary>
    public MethodInfo MethodInfo { get; set; } = null!;

    /// <summary>
    /// 工具版本
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 所需权限列表
    /// </summary>
    public IReadOnlyList<string> RequiredPermissions { get; set; } = [];

    /// <summary>
    /// 工具分类
    /// </summary>
    public string? Category { get; set; }
}
