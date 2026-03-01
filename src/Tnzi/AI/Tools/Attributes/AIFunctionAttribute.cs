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
