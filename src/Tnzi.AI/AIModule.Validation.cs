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

        if (options.BuiltInTools.Enabled && options.BuiltInTools.EnableWebSearch
            && serviceProvider.GetService<IWebSearchProvider>() == null)
        {
            throw new InvalidOperationException(
                "AI:BuiltInTools:EnableWebSearch is enabled, but no IWebSearchProvider is registered.");
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

        var workflowService = serviceProvider.GetRequiredService<IWorkflowService>();
        if (workflowService is INoOpService)
        {
            logger.LogInformation(
                "IWorkflowService is a no-op fallback; workflow APIs will return 501 until a workflow module is loaded.");
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

        logger.LogDebug("AI runtime configuration validation passed.");
    }

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
