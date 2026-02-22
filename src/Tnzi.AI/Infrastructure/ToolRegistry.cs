

namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 工具注册表 - 存储和管理所有工具定义
/// </summary>
public class ToolRegistry
{
    private readonly ConcurrentDictionary<string, List<ToolDefinition>> _toolsByGroup = new();
    private readonly ConcurrentDictionary<string, ToolDefinition> _toolsByName = new();
    private readonly ILogger<ToolRegistry> _logger;
    private readonly object _lock = new();

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 注册工具（线程安全）
    /// </summary>
    public void Register(ToolDefinition tool)
    {
        Check.NotNull(tool);

        lock (_lock)
        {
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
            if (string.IsNullOrWhiteSpace(tool.RequiredPermissions))
            {
                filteredTools.Add(tool);
                continue;
            }

            // 解析工具所需权限
            try
            {
                var requiredPerms = System.Text.Json.JsonSerializer.Deserialize<List<string>>(tool.RequiredPermissions);
                if (requiredPerms == null || requiredPerms.Count == 0)
                {
                    filteredTools.Add(tool);
                    continue;
                }

                // 检查用户是否有所有必需的权限
                bool hasAllPermissions = requiredPerms.All(perm => permissionsSet.Contains(perm.Trim()));
                if (hasAllPermissions)
                {
                    filteredTools.Add(tool);
                }
            }
            catch
            {
                // 如果解析失败，允许访问（向后兼容）
                filteredTools.Add(tool);
            }
        }

        return filteredTools;
    }
}
