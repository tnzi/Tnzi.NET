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
    /// 移除单个工具（线程安全）
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <returns>如果工具存在并已移除返回 true，否则返回 false</returns>
    bool UnregisterTool(string toolName) => false;

    /// <summary>
    /// 移除指定提供者类型注册的所有工具（线程安全，精确匹配 ProviderType）
    /// </summary>
    /// <param name="providerType">工具提供者类型（如 typeof(DateTimeTools)）</param>
    void UnregisterByProviderType(Type providerType) { }

    /// <summary>
    /// 根据权限过滤工具
    /// </summary>
    /// <param name="groupNames">工具组名称列表</param>
    /// <param name="userPermissions">
    /// 用户持有的权限集合。<c>null</c> = 调用方明确放弃门控（系统 / 内部路径，返回全部工具）；
    /// <b>空集 = 该用户一条权限都没有</b>，凡声明了 <see cref="ToolDefinition.RequiredPermissions"/>
    /// 的工具一律排除。两者绝不能混同：把空集当「不过滤」会让零权限用户拿到比持部分权限的用户更多的工具。
    /// </param>
    /// <returns>用户有权限访问的工具列表</returns>
    IReadOnlyList<ToolDefinition> GetToolsByGroupsWithPermissions(
        IEnumerable<string> groupNames,
        IEnumerable<string>? userPermissions = null);

    /// <summary>
    /// 按工具<b>名称</b>解析单个工具（per-tool 授权用），并按用户权限过滤。
    /// 与 <see cref="GetToolsByGroupsWithPermissions"/> 对称：未知名称跳过，权限不足的工具被排除
    /// （授权是允许哪些工具的白名单，权限仍然门控访问）。
    /// Resolves individual tools by NAME (for per-tool grants), permission-filtered - symmetric with
    /// <see cref="GetToolsByGroupsWithPermissions"/>: unknown names skipped, permission-gated tools excluded
    /// (a grant is an allow-list of WHICH tools; permissions still gate access).
    /// 默认实现遍历 <see cref="GetAllTools"/> 按名称匹配，保持现有实现兼容。
    /// </summary>
    /// <param name="toolNames">工具名称列表（未知名称跳过）</param>
    /// <param name="userPermissions">
    /// 用户持有的权限集合；语义与 <see cref="GetToolsByGroupsWithPermissions"/> 相同：
    /// <c>null</c> 不过滤，空集排除全部门控工具。
    /// </param>
    IReadOnlyList<ToolDefinition> GetToolsByNames(
        IEnumerable<string> toolNames,
        IEnumerable<string>? userPermissions = null)
    {
        var nameSet = new HashSet<string>(toolNames, StringComparer.OrdinalIgnoreCase);
        if (nameSet.Count == 0) return Array.Empty<ToolDefinition>();

        var permissionsSet = userPermissions != null
            ? new HashSet<string>(userPermissions, StringComparer.OrdinalIgnoreCase)
            : null;

        var matched = GetAllTools().Where(t => nameSet.Contains(t.Name));

        // 只有 null 表示放弃门控；空集是「没有任何权限」，必须走过滤
        if (permissionsSet == null)
        {
            return matched.ToList();
        }

        return matched
            .Where(t => t.RequiredPermissions.Count == 0
                || t.RequiredPermissions.All(permissionsSet.Contains))
            .ToList();
    }
}
