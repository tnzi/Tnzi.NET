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
    /// <remarks>
    /// 同一批工具调用全部标了它（且都不是破坏性的）时，执行器用 <c>Task.WhenAll</c> 并行跑这一批。
    /// 并行的调用从同一个运行的请求作用域解析工具提供者，共用同一个作用域服务实例，所以只有满足下面两条的工具才能标：
    /// 不经作用域服务访问数据库（<c>DbContext</c> / <c>IRepository</c> 及建在它们上面的服务不允许同一实例上并发操作，
    /// 第二个调用会抛 "A second operation was started on this context instance"）；
    /// 不写运行共享的可变状态（例如运行属性包），或者写入与调用顺序无关。
    /// 只读不等于可并发：查库的只读工具照样不能标。
    /// </remarks>
    public bool IsConcurrencySafe { get; set; }

    /// <summary>
    /// 是否为破坏性操作（删除文件、清除数据等不可逆操作）。默认 false。
    /// </summary>
    public bool IsDestructive { get; set; }

    /// <summary>
    /// 搜索提示 - 提供额外匹配关键词用于工具搜索评分
    /// </summary>
    public string? SearchHint { get; set; }

    /// <summary>
    /// 工具别名（逗号分隔），用于工具名称匹配
    /// </summary>
    public string? Aliases { get; set; }

    /// <summary>
    /// 工具中断行为 - 当 Agent 取消时工具如何响应。默认 Cancel（立即取消）。
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
