namespace Tnzi.AI.Tools.Attributes;

/// <summary>
/// 标记函数参数特性
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public class AIParameterAttribute : Attribute
{
    /// <summary>
    /// 参数名称（如果为空，使用参数名）
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 参数描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否必需
    /// </summary>
    public bool Required { get; set; } = true;

    /// <summary>
    /// 初始化参数特性
    /// </summary>
    /// <param name="name">参数名称</param>
    /// <param name="description">参数描述</param>
    public AIParameterAttribute(string name, string description)
    {
        Name = Check.NotNull(name);
        Description = Check.NotNull(description);
    }

    /// <summary>
    /// 初始化参数特性
    /// </summary>
    /// <param name="name">参数名称</param>
    /// <param name="description">参数描述</param>
    /// <param name="required">是否必需</param>
    public AIParameterAttribute(string name, string description, bool required)
        : this(name, description)
    {
        Required = required;
    }
}
