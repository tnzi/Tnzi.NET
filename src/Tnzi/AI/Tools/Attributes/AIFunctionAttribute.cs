namespace Tnzi.AI.Tools.Attributes;

/// <summary>
/// 标记 AI 函数特性
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class AIFunctionAttribute : Attribute
{
    /// <summary>
    /// 函数名称（如果为空，使用方法名）
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 函数描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 工具版本
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 所需权限（用逗号分隔的权限列表）
    /// </summary>
    public string? RequiredPermissions { get; set; }

    /// <summary>
    /// 工具分类
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// 是否为只读操作（不会修改任何状态）。默认 false（fail-closed）。
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// 是否可安全并发执行（多个实例同时运行不会产生竞态条件）。默认 false（fail-closed）。
    /// </summary>
    public bool IsConcurrencySafe { get; set; }

    /// <summary>
    /// 是否为破坏性操作（删除文件、清除数据等不可逆操作）。默认 false。
    /// </summary>
    public bool IsDestructive { get; set; }

    /// <summary>
    /// 工具结果最大字符数限制。0 = 不限制（默认）。
    /// </summary>
    public int MaxResultSizeChars { get; set; }

    /// <summary>
    /// 搜索提示 — 提供额外匹配关键词用于工具搜索评分
    /// </summary>
    public string? SearchHint { get; set; }

    /// <summary>
    /// 工具别名（逗号分隔），用于工具名称匹配
    /// </summary>
    public string? Aliases { get; set; }

    /// <summary>
    /// 搜索优先级。值越高越靠前。默认 0。
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 工具中断行为 — 当 Agent 取消时工具如何响应。默认 Cancel（立即取消）。
    /// </summary>
    public ToolInterruptBehavior InterruptBehavior { get; set; } = ToolInterruptBehavior.Cancel;

    /// <summary>
    /// 初始化函数特性
    /// </summary>
    public AIFunctionAttribute()
    {
    }

    /// <summary>
    /// 初始化函数特性
    /// </summary>
    /// <param name="description">函数描述</param>
    public AIFunctionAttribute(string description)
    {
        Description = Check.NotNull(description);
    }

    /// <summary>
    /// 初始化函数特性
    /// </summary>
    /// <param name="name">函数名称</param>
    /// <param name="description">函数描述</param>
    public AIFunctionAttribute(string name, string description)
    {
        Name = Check.NotNull(name);
        Description = Check.NotNull(description);
    }
}
