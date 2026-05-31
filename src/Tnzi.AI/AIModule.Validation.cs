namespace Tnzi.AI;

/// <summary>
/// Startup validation and tool-registration helpers for <see cref="AIModule"/>.
/// Extracted from AIModule.cs to reduce the main module file's surface area.
/// All methods here are pure helpers invoked from <see cref="AIModule.OnApplicationInitializationAsync"/>.
/// </summary>
public partial class AIModule
{
    private static void ValidateRuntimeConfiguration(IServiceProvider serviceProvider, ILogger logger)
    {
        var options = serviceProvider.GetRequiredService<IOptions<AIOptions>>().Value;
        var hasEnabledProvider = options.Providers.Values.Any(p => p.Enabled);

        if (options.History.Reduction.Mode == HistoryReductionMode.Summarize && !hasEnabledProvider)
        {
            throw new InvalidOperationException(
                "AI:History:Reduction:Mode is set to Summarize, but no enabled AI provider is configured.");
        }

        if (options.Guardrails.Enabled && options.Guardrails.LlmJudge.Enabled && !hasEnabledProvider)
        {
            throw new InvalidOperationException(
                "AI:Guardrails:LlmJudge is enabled, but no enabled AI provider is configured.");
        }

        if (options.ContextProviders.Enabled && options.ContextProviders.EntityMemory.Enabled && !hasEnabledProvider)
        {
            throw new InvalidOperationException(
                "AI:ContextProviders:EntityMemory is enabled, but no enabled AI provider is configured for entity extraction.");
        }

        if (options.BuiltInTools.Enabled && options.BuiltInTools.EnableWebSearch)
        {
            // Resolve through a scope so a Scoped IWebSearchProvider registration is
            // also detected (matches the ISkillRegistry probe pattern below).
            using var scope = serviceProvider.CreateScope();
            if (scope.ServiceProvider.GetService<IWebSearchProvider>() == null)
            {
                throw new InvalidOperationException(
                    "AI:BuiltInTools:EnableWebSearch is enabled, but no IWebSearchProvider is registered.");
            }
        }

        if (options.ContextProviders.Enabled)
        {
            using var scope = serviceProvider.CreateScope();
            var textSearchService = scope.ServiceProvider.GetRequiredService<ITextSearchService>();
            if (textSearchService is INoOpService
                && (options.ContextProviders.TextSearch.Enabled || options.ContextProviders.ChatHistoryMemory.Enabled))
            {
                throw new InvalidOperationException(
                    "AI text search or chat history memory is enabled, but ITextSearchService is still a no-op fallback. Register a real ITextSearchService implementation.");
            }

            // Paths 为空时不再报错 — FileSystemSkillStore 有自动发现机制（扫描模块程序集目录的 Skills/ 文件夹）
        }

        if (options.ContextProviders.Enabled
            && options.ContextProviders.Skills.Enabled)
        {
            using var scope = serviceProvider.CreateScope();
            if (scope.ServiceProvider.GetService<ISkillRegistry>() == null)
            {
                logger.LogWarning(
                    "AI skills context provider is enabled but no ISkillRegistry is registered. Skill context injection will be unavailable.");
            }
        }

        // 统一探测所有可选子模块的 NoOp 回退实现（仅信息级日志，不抛出）。
        // 每项对应 AIModule.ConfigureServicesAsync 中的一个 TryAdd NoOp 注册；
        // 加载相应子模块后会被真实实现覆盖。新增 NoOp 回退时必须在此追加一行。
        // 注意：ITextSearchService 的 NoOp 回退是有条件的硬错误（见上方），不在此列。
        foreach (var (serviceType, message) in NoOpFallbackProbes)
        {
            using var scope = serviceProvider.CreateScope();
            if (scope.ServiceProvider.GetService(serviceType) is INoOpService)
            {
                logger.LogInformation("{Message}", message);
            }
        }

        logger.LogDebug("AI runtime configuration validation passed.");
    }

    /// <summary>
    /// 可选子模块 NoOp 回退探测表 — 覆盖 <see cref="INoOpService"/> 的全部回退注册。
    /// </summary>
    /// <remarks>
    /// 不包含 <c>ITextSearchService</c>：当 TextSearch/ChatHistoryMemory 启用却仍是 NoOp 时，
    /// 它是一个有条件的硬错误（抛出），而非信息级降级。
    /// </remarks>
    private static readonly (Type ServiceType, string Message)[] NoOpFallbackProbes =
    [
        (typeof(IWorkflowService),
            "IWorkflowService is a no-op fallback; workflow APIs will return 501 until Tnzi.AI.Workflow module is loaded."),
        (typeof(IWorkflowExecutionControlService),
            "IWorkflowExecutionControlService is a no-op fallback; workflow run control will be unavailable until Tnzi.AI.Workflow module is loaded."),
        (typeof(IWorkflowExecutionQueryService),
            "IWorkflowExecutionQueryService is a no-op fallback; workflow run queries will be unavailable until Tnzi.AI.Workflow module is loaded."),
        (typeof(IWorkflowExecutionMailbox),
            "IWorkflowExecutionMailbox is a no-op fallback; workflow signal dispatch will be unavailable until Tnzi.AI.Workflow module is loaded."),
        (typeof(IExternalCliExecutor),
            "IExternalCliExecutor is a no-op fallback; ExternalCli agents will fail at runtime until Tnzi.AI.Cli module is loaded."),
        (typeof(IAgentStreamForwarder),
            "IAgentStreamForwarder is a no-op fallback; cross-process agent stream forwarding will be unavailable until a forwarder is registered."),
        (typeof(IVectorStore),
            "IVectorStore is a no-op fallback; vector search will return empty results until Tnzi.AI.Rag module is loaded."),
        (typeof(ISkillLoadTracker),
            "ISkillLoadTracker is a no-op fallback; skill load tracking will be unavailable until Tnzi.AI.Skills module is loaded."),
        (typeof(ISkillService),
            "ISkillService is a no-op fallback; skill management APIs will return 501 until Tnzi.AI.Skills module is loaded."),
        (typeof(ISkillStore),
            "ISkillStore is a no-op fallback; skill storage will be unavailable until Tnzi.AI.Skills module is loaded."),
        (typeof(ISkillTemplateEngine),
            "ISkillTemplateEngine is a no-op fallback; skill template rendering will be unavailable until Tnzi.AI.Skills module is loaded."),
        (typeof(ISkillConstraintEnforcer),
            "ISkillConstraintEnforcer is a no-op fallback; skill constraint enforcement will be unavailable until Tnzi.AI.Skills module is loaded."),
        (typeof(ISkillSearchService),
            "ISkillSearchService is a no-op fallback; skill search will be unavailable until Tnzi.AI.Skills module is loaded."),
        (typeof(ISkillRequirementsValidator),
            "ISkillRequirementsValidator is a no-op fallback; skill requirements validation will be unavailable until Tnzi.AI.Skills module is loaded."),
    ];

    /// <summary>
    /// 校验所有工具的 [RequiresSkill] 引用是否指向已注册的 Skill
    /// </summary>
    private static async Task ValidateRequiresSkillReferencesAsync(IToolRegistry toolRegistry, IServiceProvider serviceProvider, ILogger logger)
    {
        var allTools = toolRegistry.GetAllTools();
        var toolsWithSkills = allTools.Where(t => t.RequiresSkillSlugs is { Count: > 0 }).ToList();
        if (toolsWithSkills.Count == 0) return;

        // ISkillRegistry is scoped — create a temporary scope for startup validation
        using var scope = serviceProvider.CreateScope();
        var skillRegistry = scope.ServiceProvider.GetService<ISkillRegistry>();
        if (skillRegistry == null) return;

        var allSlugs = toolsWithSkills.SelectMany(t => t.RequiresSkillSlugs!).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in allSlugs)
        {
            var skill = await skillRegistry.GetBySlugAsync(slug, CancellationToken.None);
            if (skill == null)
            {
                logger.LogWarning(
                    "[RequiresSkill] references skill '{Slug}' which does not exist. " +
                    "Tools referencing this skill: {Tools}. Check for typos or missing SKILL.md files.",
                    slug,
                    string.Join(", ", toolsWithSkills.Where(t => t.RequiresSkillSlugs!.Contains(slug, StringComparer.OrdinalIgnoreCase)).Select(t => t.Name)));
            }
        }
    }

    /// <summary>
    /// 扫描程序集并注册工具到 ToolRegistry
    /// </summary>
    private static void RegisterTools(IToolRegistry registry, IToolScanner scanner, Assembly assembly, ILogger logger)
    {
        try
        {
            var tools = scanner.ScanAssembly(assembly);
            foreach (var tool in tools)
            {
                registry.Register(tool);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to scan assembly '{AssemblyName}' for AI tools. Skipping this assembly.",
                assembly.FullName);
        }
    }

    /// <summary>
    /// 判断是否为框架核心程序集
    /// </summary>
    /// <remarks>
    /// 所有 Tnzi.* 程序集（包括 Tnzi.AI.Coder）由各自模块负责工具注册。
    /// AIModule 只扫描自身程序集和用户应用程序集。
    /// </remarks>
    private static bool IsFrameworkCoreAssembly(Assembly a)
    {
        var name = a.GetName().Name;
        if (name == null) return false;
        return name.StartsWith("Tnzi.", StringComparison.Ordinal);
    }
}
