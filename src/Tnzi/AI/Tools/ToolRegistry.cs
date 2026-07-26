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
        // 必须在 _lock 内复制：组内的 List 由 Register/Unregister* 在锁内就地增删，
        // 无锁 ToList() 与并发 Add/Remove 竞争会抛 InvalidOperationException 或复制出撕裂的快照
        lock (_lock)
        {
            if (_toolsByGroup.TryGetValue(groupName, out var tools))
            {
                // 返回副本，防止调用方遍历时被并发修改
                return tools.ToList();
            }
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
    /// 移除单个工具（线程安全）
    /// </summary>
    public bool UnregisterTool(string toolName)
    {
        Check.NotNullOrWhiteSpace(toolName);

        lock (_lock)
        {
            if (!_toolsByName.TryRemove(toolName, out var removed))
            {
                return false;
            }

            if (_toolsByGroup.TryGetValue(removed.GroupName, out var groupTools))
            {
                groupTools.RemoveAll(t => t.Name == toolName);

                // 如果组内已无工具，移除空组
                if (groupTools.Count == 0)
                {
                    _toolsByGroup.TryRemove(removed.GroupName, out _);
                }
            }

            _logger.LogDebug("Unregistered tool '{ToolName}' from group '{GroupName}'", toolName, removed.GroupName);
            return true;
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

    /// <summary>
    /// 按工具名称解析单个工具（per-tool 授权），并按用户权限过滤。
    /// 使用 <c>_toolsByName</c> 索引按名称 O(1) 查找，未知名称跳过；权限不足的工具被排除。
    /// </summary>
    public IReadOnlyList<ToolDefinition> GetToolsByNames(
        IEnumerable<string> toolNames,
        IEnumerable<string>? userPermissions = null)
    {
        Check.NotNull(toolNames);

        var permissionsSet = userPermissions != null
            ? new HashSet<string>(userPermissions, StringComparer.OrdinalIgnoreCase)
            : null;

        var result = new List<ToolDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in toolNames)
        {
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) continue;
            if (!_toolsByName.TryGetValue(name, out var tool)) continue; // 未知名称跳过

            // 权限门控：无权限要求或用户拥有全部必需权限时放行
            if (permissionsSet == null || permissionsSet.Count == 0
                || tool.RequiredPermissions.Count == 0
                || tool.RequiredPermissions.All(permissionsSet.Contains))
            {
                result.Add(tool);
            }
        }

        return result;
    }
}
