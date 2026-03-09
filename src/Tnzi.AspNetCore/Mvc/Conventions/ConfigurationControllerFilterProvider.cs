namespace Tnzi.AspNetCore.Mvc.Conventions;

/// <summary>
/// 配置化 Controller 过滤提供者
/// 根据 <see cref="ControllerFilterOptions"/> 配置按名称通配符、程序集名称或自定义谓词过滤 Controller
/// </summary>
public class ConfigurationControllerFilterProvider : IApplicationModelProvider
{
    private readonly ControllerFilterOptions _options;
    private readonly Regex[]? _disabledControllerPatterns;
    private readonly Regex[]? _disabledAssemblyPatterns;

    /// <summary>
    /// 执行顺序，在 ConditionalControllerProvider(-500) 之后执行
    /// </summary>
    public int Order => -400;

    /// <summary>
    /// 初始化配置化 Controller 过滤提供者
    /// </summary>
    /// <param name="options">Controller 过滤选项</param>
    public ConfigurationControllerFilterProvider(ControllerFilterOptions options)
    {
        _options = Check.NotNull(options);

        // 预编译通配符为正则表达式，避免每次请求重复编译
        _disabledControllerPatterns = CompilePatterns(options.DisabledControllers);
        _disabledAssemblyPatterns = CompilePatterns(options.DisabledAssemblies);
    }

    /// <summary>
    /// 在其他提供者执行前调用，根据配置过滤 Controller
    /// </summary>
    public void OnProvidersExecuting(ApplicationModelProviderContext context)
    {
        var controllersToRemove = new List<ControllerModel>();

        foreach (var controller in context.Result.Controllers)
        {
            var controllerType = controller.ControllerType.AsType();

            // 1. 按程序集名称过滤
            if (_disabledAssemblyPatterns != null)
            {
                var assemblyName = controllerType.Assembly.GetName().Name ?? string.Empty;
                if (MatchesAnyPattern(assemblyName, _disabledAssemblyPatterns))
                {
                    controllersToRemove.Add(controller);
                    continue;
                }
            }

            // 2. 按 Controller 类名过滤
            if (_disabledControllerPatterns != null)
            {
                var controllerName = controllerType.Name;
                if (MatchesAnyPattern(controllerName, _disabledControllerPatterns))
                {
                    controllersToRemove.Add(controller);
                    continue;
                }
            }

            // 3. 自定义谓词过滤（返回 false 表示移除）
            if (_options.ControllerPredicate != null && !_options.ControllerPredicate(controllerType))
            {
                controllersToRemove.Add(controller);
            }
        }

        foreach (var controller in controllersToRemove)
        {
            context.Result.Controllers.Remove(controller);
        }
    }

    /// <summary>
    /// 在其他提供者执行后调用（不需要实现）
    /// </summary>
    public void OnProvidersExecuted(ApplicationModelProviderContext context)
    {
    }

    /// <summary>
    /// 将通配符模式数组编译为正则表达式数组
    /// </summary>
    private static Regex[]? CompilePatterns(string[]? patterns)
    {
        if (patterns == null || patterns.Length == 0)
            return null;

        var regexes = new Regex[patterns.Length];
        for (var i = 0; i < patterns.Length; i++)
        {
            // 将通配符 * 转换为正则 .*，转义其他特殊字符
            var escaped = Regex.Escape(patterns[i]).Replace("\\*", ".*");
            regexes[i] = new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        return regexes;
    }

    /// <summary>
    /// 检查名称是否匹配任意一个模式
    /// </summary>
    private static bool MatchesAnyPattern(string name, Regex[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.IsMatch(name))
                return true;
        }
        return false;
    }
}
