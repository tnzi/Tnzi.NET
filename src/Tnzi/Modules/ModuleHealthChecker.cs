
namespace Tnzi.Modules;

/// <summary>
/// 模块健康检查器
/// 用于检查模块依赖完整性和初始化状态
/// </summary>
public class ModuleHealthChecker
{
    private readonly ILogger<ModuleHealthChecker>? _logger;

    /// <summary>
    /// 初始化模块健康检查器
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    public ModuleHealthChecker(ILogger<ModuleHealthChecker>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 检查模块依赖完整性
    /// </summary>
    /// <param name="modules">模块列表</param>
    /// <returns>健康检查结果</returns>
    public ModuleHealthCheckResult CheckDependencies(IReadOnlyList<IModuleDescriptor> modules)
    {
        var result = new ModuleHealthCheckResult();
        var moduleDict = modules.ToDictionary(m => m.Type, m => m);

        foreach (var module in modules)
        {
            var dependsOnAttributes = module.Type.GetCustomAttributes<DependsOnAttribute>();
            var missingDependencies = new List<Type>();

            foreach (var attribute in dependsOnAttributes)
            {
                foreach (var dependedModuleType in attribute.DependedModuleTypes)
                {
                    if (!moduleDict.ContainsKey(dependedModuleType))
                    {
                        missingDependencies.Add(dependedModuleType);
                    }
                }
            }

            if (missingDependencies.Count > 0)
            {
                var issue = new ModuleHealthIssue
                {
                    ModuleType = module.Type,
                    IssueType = ModuleHealthIssueType.MissingDependency,
                    Message = $"Module {module.Type.Name} has missing dependencies: {string.Join(", ", missingDependencies.Select(t => t.Name))}",
                    MissingDependencies = missingDependencies
                };
                result.Issues.Add(issue);
            }
        }

        result.IsHealthy = result.Issues.Count == 0;

        if (!result.IsHealthy)
        {
            _logger?.LogWarning("Module dependency check found {Count} issues", result.Issues.Count);
        }
        else
        {
            _logger?.LogDebug("All module dependencies are satisfied");
        }

        return result;
    }

    /// <summary>
    /// 检查模块初始化状态
    /// </summary>
    /// <param name="modules">模块列表</param>
    /// <returns>健康检查结果</returns>
    public ModuleHealthCheckResult CheckInitializationStatus(IReadOnlyList<IModuleDescriptor> modules)
    {
        var result = new ModuleHealthCheckResult();

        foreach (var module in modules)
        {
            if (module is ModuleDescriptor descriptor)
            {
                // 检查模块是否已初始化
                if (!descriptor.IsEnabled && descriptor.InitializationEndTime == null)
                {
                    var issue = new ModuleHealthIssue
                    {
                        ModuleType = module.Type,
                        IssueType = ModuleHealthIssueType.NotInitialized,
                        Message = $"Module {module.Type.Name} has not been initialized"
                    };
                    result.Issues.Add(issue);
                }
                // 检查模块初始化是否失败
                else if (!descriptor.IsEnabled && descriptor.InitializationEndTime != null)
                {
                    var issue = new ModuleHealthIssue
                    {
                        ModuleType = module.Type,
                        IssueType = ModuleHealthIssueType.InitializationFailed,
                        Message = $"Module {module.Type.Name} initialization failed"
                    };
                    result.Issues.Add(issue);
                }
            }
        }

        result.IsHealthy = result.Issues.Count == 0;

        if (!result.IsHealthy)
        {
            _logger?.LogWarning("Module initialization check found {Count} issues", result.Issues.Count);
        }
        else
        {
            _logger?.LogDebug("All modules are properly initialized");
        }

        return result;
    }

    /// <summary>
    /// 执行完整的健康检查
    /// </summary>
    /// <param name="modules">模块列表</param>
    /// <returns>健康检查结果</returns>
    public ModuleHealthCheckResult CheckAll(IReadOnlyList<IModuleDescriptor> modules)
    {
        var dependencyResult = CheckDependencies(modules);
        var initializationResult = CheckInitializationStatus(modules);

        var combinedResult = new ModuleHealthCheckResult
        {
            IsHealthy = dependencyResult.IsHealthy && initializationResult.IsHealthy
        };
        combinedResult.Issues.AddRange(dependencyResult.Issues);
        combinedResult.Issues.AddRange(initializationResult.Issues);

        return combinedResult;
    }
}

/// <summary>
/// 模块健康检查结果
/// </summary>
public class ModuleHealthCheckResult
{
    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; set; } = true;

    /// <summary>
    /// 健康问题列表
    /// </summary>
    public List<ModuleHealthIssue> Issues { get; } = new();
}

/// <summary>
/// 模块健康问题
/// </summary>
public class ModuleHealthIssue
{
    /// <summary>
    /// 模块类型
    /// </summary>
    public Type ModuleType { get; set; } = null!;

    /// <summary>
    /// 问题类型
    /// </summary>
    public ModuleHealthIssueType IssueType { get; set; }

    /// <summary>
    /// 问题消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 缺失的依赖（如果是缺失依赖问题）
    /// </summary>
    public IReadOnlyList<Type>? MissingDependencies { get; set; }
}

/// <summary>
/// 模块健康问题类型
/// </summary>
public enum ModuleHealthIssueType
{
    /// <summary>
    /// 缺失依赖
    /// </summary>
    MissingDependency,

    /// <summary>
    /// 未初始化
    /// </summary>
    NotInitialized,

    /// <summary>
    /// 初始化失败
    /// </summary>
    InitializationFailed
}