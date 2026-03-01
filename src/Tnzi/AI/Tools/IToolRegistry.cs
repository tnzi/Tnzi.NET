namespace Tnzi.AI.Tools;

/// <summary>
/// 工具注册表接口 - 存储和管理所有工具定义
/// </summary>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public interface IToolRegistry
{
    /// <summary>
    /// 注册工具（线程安全，按名称去重）
    /// </summary>
    void Register(ToolDefinition tool);

    /// <summary>
    /// 根据工具组获取工具
    /// </summary>
    IReadOnlyList<ToolDefinition> GetToolsByGroup(string groupName);

    /// <summary>
    /// 根据工具组列表获取工具
    /// </summary>
    IReadOnlyList<ToolDefinition> GetToolsByGroups(IEnumerable<string> groupNames);

    /// <summary>
    /// 获取所有工具组名称
    /// </summary>
    IEnumerable<string> GetAllGroupNames();

    /// <summary>
    /// 获取所有工具
    /// </summary>
    IReadOnlyList<ToolDefinition> GetAllTools();

    /// <summary>
    /// 移除指定工具组的所有工具（线程安全）
    /// </summary>
    /// <param name="groupName">工具组名称</param>
    void UnregisterGroup(string groupName) { }

    /// <summary>
    /// 移除指定提供者类型注册的所有工具（线程安全，精确匹配 ProviderType）
    /// </summary>
    /// <param name="providerType">工具提供者类型（如 typeof(DateTimeTools)）</param>
    void UnregisterByProviderType(Type providerType) { }

    /// <summary>
    /// 根据权限过滤工具
    /// </summary>
    /// <param name="groupNames">工具组名称列表</param>
    /// <param name="userPermissions">用户权限列表</param>
    /// <returns>用户有权限访问的工具列表</returns>
    IReadOnlyList<ToolDefinition> GetToolsByGroupsWithPermissions(
        IEnumerable<string> groupNames,
        IEnumerable<string>? userPermissions = null);
}
