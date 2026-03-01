namespace Tnzi.AI.Tools.Attributes;

/// <summary>
/// 标记工具组特性
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class AIToolGroupAttribute : Attribute
{
    /// <summary>
    /// 工具组名称
    /// </summary>
    public string GroupName { get; }

    /// <summary>
    /// 显示名称
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 初始化工具组特性
    /// </summary>
    /// <param name="groupName">工具组名称</param>
    public AIToolGroupAttribute(string groupName)
    {
        GroupName = Check.NotNull(groupName);
    }

    /// <summary>
    /// 初始化工具组特性
    /// </summary>
    /// <param name="groupName">工具组名称</param>
    /// <param name="displayName">显示名称</param>
    public AIToolGroupAttribute(string groupName, string displayName)
        : this(groupName)
    {
        DisplayName = displayName;
    }

    /// <summary>
    /// 初始化工具组特性
    /// </summary>
    /// <param name="groupName">工具组名称</param>
    /// <param name="displayName">显示名称</param>
    /// <param name="description">描述</param>
    public AIToolGroupAttribute(string groupName, string displayName, string description)
        : this(groupName, displayName)
    {
        Description = description;
    }
}
