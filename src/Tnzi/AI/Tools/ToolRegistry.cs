namespace Tnzi.AI.Tools;

/// <summary>
/// 工具注册表 - 存储和管理所有工具定义
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, List<ToolDefinition>> _toolsByGroup = new();
    private readonly ConcurrentDictionary<string, ToolDefinition> _toolsByName = new();
    private readonly ILogger<ToolRegistry> _logger;
    private readonly object _lock = new();

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 注册工具（线程安全，按名称去重）
    /// </summary>
    public void Register(ToolDefinition tool)
    {
        Check.NotNull(tool);

        lock (_lock)
        {
            // 按名称去重：如果已存在同名工具，先从旧分组中移除
            if (_toolsByName.TryGetValue(tool.Name, out var existing))
            {
                if (_toolsByGroup.TryGetValue(existing.GroupName, out var oldGroup))
                {
                    oldGroup.RemoveAll(t => t.Name == tool.Name);
                }
            }

            // 按工具组组织
            var groupTools = _toolsByGroup.GetOrAdd(tool.GroupName, _ => new List<ToolDefinition>());
            groupTools.Add(tool);

            // 按名称索引
            _toolsByName[tool.Name] = tool;

            _logger.LogDebug("Registered tool '{ToolName}' in group '{GroupName}'",
                tool.Name, tool.GroupName);
        }
    }

    /// <summary>
    /// 根据工具组获取工具
    /// </summary>
    public IReadOnlyList<ToolDefinition> GetToolsByGroup(string groupName)
    {
        if (_toolsByGroup.TryGetValue(groupName, out var tools))
        {
            return tools;
        }

        return Array.Empty<ToolDefinition>();
    }

    /// <summary>
    /// 根据工具组列表获取工具
    /// </summary>
    public IReadOnlyList<ToolDefinition> GetToolsByGroups(IEnumerable<string> groupNames)
    {
        var tools = new List<ToolDefinition>();
        var addedNames = new HashSet<string>();

        foreach (var groupName in groupNames)
        {
            var groupTools = GetToolsByGroup(groupName);
            foreach (var tool in groupTools)
            {
                if (addedNames.Add(tool.Name))
                {
                    tools.Add(tool);
                }
            }
        }

        return tools;
    }

    /// <summary>
    /// 获取所有工具组名称
    /// </summary>
    public IEnumerable<string> GetAllGroupNames()
    {
        return _toolsByGroup.Keys;
    }

    /// <summary>
    /// 获取所有工具
    /// </summary>
    public IReadOnlyList<ToolDefinition> GetAllTools()
    {
        return _toolsByName.Values.ToList();
    }

    /// <summary>
    /// 移除指定工具组的所有工具（线程安全）
    /// </summary>
    public void UnregisterGroup(string groupName)
    {
        Check.NotNullOrWhiteSpace(groupName);

        lock (_lock)
        {
            if (_toolsByGroup.TryRemove(groupName, out var removedTools))
            {
                foreach (var tool in removedTools)
                {
                    _toolsByName.TryRemove(tool.Name, out _);
                }
                _logger.LogDebug("Unregistered tool group '{GroupName}' ({Count} tools)", groupName, removedTools.Count);
            }
        }
    }

    /// <summary>
    /// 移除指定提供者类型注册的所有工具（线程安全，精确匹配 ProviderType）
    /// </summary>
    public void UnregisterByProviderType(Type providerType)
    {
        Check.NotNull(providerType);

        lock (_lock)
        {
            var removedCount = 0;

            foreach (var (groupName, groupTools) in _toolsByGroup)
            {
                var toRemove = groupTools.Where(t => t.ProviderType == providerType).ToList();
                foreach (var tool in toRemove)
                {
                    groupTools.Remove(tool);
                    _toolsByName.TryRemove(tool.Name, out _);
                    removedCount++;
                }

                // 如果组内已无工具，移除空组
                if (groupTools.Count == 0)
                {
                    _toolsByGroup.TryRemove(groupName, out _);
                }
            }

            if (removedCount > 0)
            {
                _logger.LogDebug("Unregistered {Count} tools from provider type '{ProviderType}'",
                    removedCount, providerType.Name);
            }
        }
    }

    /// <summary>
    /// 根据权限过滤工具
    /// </summary>
    /// <param name="groupNames">工具组名称列表</param>
    /// <param name="userPermissions">用户权限列表</param>
    /// <returns>用户有权限访问的工具列表</returns>
    public IReadOnlyList<ToolDefinition> GetToolsByGroupsWithPermissions(
        IEnumerable<string> groupNames,
        IEnumerable<string>? userPermissions = null)
    {
        var allTools = GetToolsByGroups(groupNames);
        var permissionsSet = userPermissions != null ? new HashSet<string>(userPermissions, StringComparer.OrdinalIgnoreCase) : null;

        // 如果没有提供权限列表，返回所有工具
        if (permissionsSet == null || permissionsSet.Count == 0)
        {
            return allTools;
        }

        // 过滤需要权限的工具
        var filteredTools = new List<ToolDefinition>();
        foreach (var tool in allTools)
        {
            // 如果工具没有权限要求，允许访问
            if (tool.RequiredPermissions.Count == 0)
            {
                filteredTools.Add(tool);
                continue;
            }

            // 检查用户是否有所有必需的权限
            if (tool.RequiredPermissions.All(perm => permissionsSet.Contains(perm)))
            {
                filteredTools.Add(tool);
            }
        }

        return filteredTools;
    }
}
