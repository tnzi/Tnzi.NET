namespace Tnzi.AI;

/// <summary>
/// AIModule 的启动期校验与探测（partial）。
/// 包含：可选子模块 NoOp 回退探测（信息级日志 + <c>ITextSearchService</c> 有条件硬错误）、
/// 引擎运行时配置校验（Provider 必需性）、<c>[RequiresSkill]</c> 引用校验、框架程序集判定。
/// 全部由 <see cref="AIModule.OnApplicationInitializationAsync"/> 调用。
/// </summary>
public partial class AIModule
{
    private static void ValidateNoOpFallbacks(IServiceProvider serviceProvider, ILogger logger)
    {
        var options = serviceProvider.GetRequiredService<IOptions<AIOptions>>().Value;

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

            // Paths 为空时不再报错 - FileSystemSkillStore 有自动发现机制（扫描模块程序集目录的 Skills/ 文件夹）
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
        // 每项对应 RegisterOptionalSubmoduleFallbacks 中的一个 TryAdd NoOp 注册；
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

        logger.LogDebug("AI NoOp fallback probing complete.");
    }

    /// <summary>
    /// 可选子模块 NoOp 回退探测表 - 覆盖 <see cref="INoOpService"/> 的全部回退注册。
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
        (typeof(ICliAgentDispatcher),
            "ICliAgentDispatcher is a no-op fallback; external CLI agent execution will return 501 until Tnzi.AI.Cli module is loaded."),
        (typeof(ICliRuntimeService),
            "ICliRuntimeService is a no-op fallback; external CLI runtime registration will return 501 until Tnzi.AI.Cli module is loaded."),
        // ICliAgentBindingService 不在此列：它的回退（BuiltInOnlyCliAgentBindingService）
        // 刻意不实现 INoOpService —— 读路径返回 null 表示「全部走内建」，那是正常状态而非降级。
    ];

    /// <summary>
    /// 校验引擎运行时配置 - 当启用需要 LLM Provider 的功能时，必须存在已启用的 Provider。
    /// </summary>
    private static void ValidateProviderRuntimeConfiguration(IServiceProvider serviceProvider, ILogger logger)
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
            // also detected (matches the ISkillRegistry probe pattern).
            using var scope = serviceProvider.CreateScope();
            if (scope.ServiceProvider.GetService<IWebSearchProvider>() == null)
            {
                throw new InvalidOperationException(
                    "AI:BuiltInTools:EnableWebSearch is enabled, but no IWebSearchProvider is registered.");
            }
        }

        logger.LogDebug("AI engine runtime configuration validation passed.");
    }

    /// <summary>
    /// 校验所有工具的 [RequiresSkill] 引用是否指向已注册的 Skill
    /// </summary>
    private static async Task ValidateRequiresSkillReferencesAsync(IToolRegistry toolRegistry, IServiceProvider serviceProvider, ILogger logger)
    {
        var allTools = toolRegistry.GetAllTools();
        var toolsWithSkills = allTools.Where(t => t.RequiresSkillSlugs is { Count: > 0 }).ToList();
        if (toolsWithSkills.Count == 0) return;

        // ISkillRegistry is scoped - create a temporary scope for startup validation
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
    /// 判断是否为框架核心程序集
    /// </summary>
    /// <remarks>
    /// 所有 Tnzi.* 程序集（包括各子模块）由各自模块负责工具注册。
    /// 本模块只扫描自身程序集和用户应用程序集。
    /// </remarks>
    private static bool IsFrameworkCoreAssembly(Assembly a)
    {
        var name = a.GetName().Name;
        if (name == null) return false;
        return name.StartsWith("Tnzi.", StringComparison.Ordinal);
    }
}
