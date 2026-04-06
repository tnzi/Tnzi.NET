namespace Tnzi.AI.Tools;

/// <summary>
/// AI 工具扫描器
/// </summary>
public class ToolScanner : IToolScanner
{
    private readonly ILogger<ToolScanner> _logger;

    public ToolScanner(ILogger<ToolScanner> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 扫描程序集，查找所有工具
    /// </summary>
    public IEnumerable<ToolDefinition> ScanAssembly(Assembly assembly)
    {
        _logger.LogDebug("Scanning assembly '{AssemblyName}' for AI tools", assembly.GetName().Name);

        var tools = new List<ToolDefinition>();

        // 查找所有实现 IAIToolProvider 且标记了 [AIToolGroup] 的类型
        var providerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => typeof(IAIToolProvider).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttribute<AIToolGroupAttribute>() != null);

        foreach (var providerType in providerTypes)
        {
            var groupAttr = providerType.GetCustomAttribute<AIToolGroupAttribute>()!;
            var providerTools = ScanProviderType(providerType, groupAttr);
            tools.AddRange(providerTools);
        }

        _logger.LogDebug("Found {ToolCount} AI tools in assembly '{AssemblyName}'",
            tools.Count, assembly.GetName().Name);

        return tools;
    }

    /// <summary>
    /// 扫描工具提供者类型
    /// </summary>
    private IEnumerable<ToolDefinition> ScanProviderType(Type providerType, AIToolGroupAttribute groupAttr)
    {
        var tools = new List<ToolDefinition>();

        // 收集类级 [RequiresSkill] 属性
        var classSlugs = providerType.GetCustomAttributes<RequiresSkillAttribute>()
            .Select(a => a.Slug)
            .ToList();

        // 查找所有标记了 [AIFunction] 的公共实例方法
        var methods = providerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<AIFunctionAttribute>() != null);

        foreach (var method in methods)
        {
            var funcAttr = method.GetCustomAttribute<AIFunctionAttribute>()!;

            if (!funcAttr.Enabled)
            {
                continue;
            }

            var toolName = funcAttr.Name ?? method.Name;
            // 如果方法名以 Async 结尾，移除它
            if (toolName.EndsWith("Async", StringComparison.OrdinalIgnoreCase))
            {
                toolName = toolName[..^5];
            }

            // 合并类级和方法级 [RequiresSkill] slug（去重）
            var methodSlugs = method.GetCustomAttributes<RequiresSkillAttribute>()
                .Select(a => a.Slug);
            var allSlugs = classSlugs.Union(methodSlugs, StringComparer.OrdinalIgnoreCase).ToList();

            var tool = new ToolDefinition
            {
                Name = toolName,
                Description = funcAttr.Description,
                GroupName = groupAttr.GroupName,
                ProviderType = providerType,
                MethodInfo = method,
                Version = funcAttr.Version,
                RequiredPermissions = funcAttr.RequiredPermissions != null
                    ? funcAttr.RequiredPermissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [],
                Category = funcAttr.Category,
                RequiresSkillSlugs = allSlugs,
                // 安全元数据（fail-closed: 默认最严格）
                IsReadOnly = funcAttr.IsReadOnly,
                IsConcurrencySafe = funcAttr.IsConcurrencySafe,
                IsDestructive = funcAttr.IsDestructive,
                MaxResultSizeChars = funcAttr.MaxResultSizeChars,
                SearchHint = funcAttr.SearchHint,
                Aliases = funcAttr.Aliases != null
                    ? funcAttr.Aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [],
                ShouldDefer = funcAttr.ShouldDefer,
                AlwaysLoad = funcAttr.AlwaysLoad,
                Priority = funcAttr.Priority,
                ServerHint = funcAttr.ServerHint,
                InterruptBehavior = funcAttr.InterruptBehavior
            };

            tools.Add(tool);

            _logger.LogDebug("Found AI tool: {ToolName} in group {GroupName}",
                tool.Name, tool.GroupName);
        }

        return tools;
    }
}
