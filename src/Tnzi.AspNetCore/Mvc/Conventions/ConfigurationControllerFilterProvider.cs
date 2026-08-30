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
    private readonly Regex[]? _disabledEndpointPatterns;
    private readonly ILogger<ConfigurationControllerFilterProvider>? _logger;

    /// <summary>
    /// 执行顺序，在 ConditionalControllerProvider(-500) 之后执行
    /// </summary>
    public int Order => -400;

    /// <summary>
    /// 初始化配置化 Controller 过滤提供者
    /// </summary>
    /// <param name="options">Controller 过滤选项</param>
    public ConfigurationControllerFilterProvider(ControllerFilterOptions options)
        : this(options, null)
    {
    }

    /// <summary>
    /// 初始化配置化 Controller 过滤提供者。
    /// </summary>
    /// <param name="options">Controller 过滤选项</param>
    /// <param name="loggerFactory">
    /// 可选：用于报告"配了却没匹配到任何端点"的抑制项。
    /// 拿不到时端点抑制照常工作，只是少一条告警 —— 不能因为容器里没有日志就改变过滤行为。
    /// </param>
    public ConfigurationControllerFilterProvider(ControllerFilterOptions options, ILoggerFactory? loggerFactory)
    {
        _options = Check.NotNull(options);
        _logger = loggerFactory?.CreateLogger<ConfigurationControllerFilterProvider>();

        // 预编译通配符为正则表达式，避免每次请求重复编译
        _disabledControllerPatterns = CompilePatterns(options.DisabledControllers);
        _disabledAssemblyPatterns = CompilePatterns(options.DisabledAssemblies);
        _disabledEndpointPatterns = CompilePatterns(options.DisabledEndpoints);
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

        // 4. 端点级抑制（在整类过滤之后：已被整体移除的控制器不必再逐个 action 看）
        if (_disabledEndpointPatterns != null)
        {
            RemoveDisabledEndpoints(context);
        }
    }

    /// <summary>
    /// 按 <see cref="SensitiveEndpointAttribute.Name"/> 摘掉单个 action。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <strong>只认标了 <see cref="SensitiveEndpointAttribute"/> 的方法。</strong>
    /// 一个能按任意方法名关端点的配置项会变成绕开代码审查改 API 表面的工具；
    /// 限定在显式标注过的端点上，被关掉的东西一定是作者预期到"有人会想关掉它"的那些。
    /// </para>
    /// <para>
    /// ★ <strong>配了却一个都没匹配到要告警。</strong>名字写错和"已经关掉了"在运行时长得一模一样，
    /// 而这正是本机制要消除的那类静默失效 —— 在自己身上重造一遍就说不过去了。
    /// </para>
    /// </remarks>
    private void RemoveDisabledEndpoints(ApplicationModelProviderContext context)
    {
        var matchedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in context.Result.Controllers)
        {
            var actionsToRemove = new List<ActionModel>();

            foreach (var action in controller.Actions)
            {
                var sensitive = action.ActionMethod
                    .GetCustomAttribute<SensitiveEndpointAttribute>(inherit: true);

                if (sensitive == null || !MatchesAnyPattern(sensitive.Name, _disabledEndpointPatterns!))
                {
                    continue;
                }

                actionsToRemove.Add(action);
                matchedNames.Add(sensitive.Name);
            }

            foreach (var action in actionsToRemove)
            {
                controller.Actions.Remove(action);
            }
        }

        WarnAboutUnmatchedEndpointFilters(matchedNames);
    }

    /// <summary>
    /// 报告没有匹配到任何端点的抑制项。
    /// </summary>
    private void WarnAboutUnmatchedEndpointFilters(HashSet<string> matchedNames)
    {
        if (_logger == null || _options.DisabledEndpoints == null)
        {
            return;
        }

        // 与 _disabledEndpointPatterns 索引一一对应（CompilePatterns 保序）
        for (var i = 0; i < _options.DisabledEndpoints.Length; i++)
        {
            var pattern = _disabledEndpointPatterns![i];
            if (matchedNames.Any(pattern.IsMatch))
            {
                continue;
            }

            _logger.LogWarning(
                "AspNetCore:ControllerFilter:DisabledEndpoints contains '{Pattern}', which matched no "
                + "endpoint. Nothing was suppressed by this entry - check the name against "
                + "GET admin/diagnostics/sensitive-endpoints, or remove it if the owning module is no longer loaded.",
                _options.DisabledEndpoints[i]);
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
