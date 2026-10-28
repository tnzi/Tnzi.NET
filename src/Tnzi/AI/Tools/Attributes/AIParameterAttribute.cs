namespace Tnzi.AI.Tools.Attributes;

/// <summary>
/// 标记函数参数特性：声明交给模型的参数名称、描述与是否必需。
/// </summary>
/// <remarks>
/// 由 AI 模块把工具转换为模型函数时读取：<see cref="Name"/> 是 schema 里的属性名（模型按它传参，
/// 绑定时映射回 C# 形参；C# 形参名同样被接受），<see cref="Description"/> 写进该属性的描述，
/// <see cref="Required"/> 决定它是否出现在 schema 的 <c>required</c> 里。
/// 声明为非必需而 C# 形参没有默认值时，模型省略它会按 <c>null</c> / 类型默认值绑定。
/// </remarks>
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
