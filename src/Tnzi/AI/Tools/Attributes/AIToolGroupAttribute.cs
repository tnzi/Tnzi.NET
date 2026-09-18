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
    /// 整组工具所需权限（逗号分隔），与每个方法上 <c>[AIFunction(RequiredPermissions = ...)]</c> 的声明取并集。
    /// 敏感工具组（子 Agent 生命周期、沙箱、远端 agent 调用）用它一条声明门住全部方法 ——
    /// 逐方法声明会在新增方法时漏掉一条而毫无症状。
    /// </summary>
    public string? RequiredPermissions { get; set; }

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
