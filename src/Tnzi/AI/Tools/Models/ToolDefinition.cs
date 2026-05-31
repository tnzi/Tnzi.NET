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

    /// <summary>
    /// 需要预先阅读的 Skill slug 列表（来自 [RequiresSkill] 属性，类级 + 方法级合并）
    /// </summary>
    public IReadOnlyList<string> RequiresSkillSlugs { get; set; } = [];

    /// <summary>
    /// 是否为只读操作（不会修改任何状态）。默认 false（fail-closed）。
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// 是否可安全并发执行。默认 false（fail-closed）。
    /// </summary>
    public bool IsConcurrencySafe { get; set; }

    /// <summary>
    /// 是否为破坏性操作。默认 false。
    /// </summary>
    public bool IsDestructive { get; set; }

    /// <summary>
    /// 工具结果最大字符数限制。0 = 不限制。
    /// </summary>
    public int MaxResultSizeChars { get; set; }

    /// <summary>
    /// 搜索提示关键词
    /// </summary>
    public string? SearchHint { get; set; }

    /// <summary>
    /// 工具别名列表
    /// </summary>
    public IReadOnlyList<string> Aliases { get; set; } = [];

    /// <summary>
    /// 搜索优先级。值越高越优先。
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 工具中断行为
    /// </summary>
    public ToolInterruptBehavior InterruptBehavior { get; set; } = ToolInterruptBehavior.Cancel;
}
