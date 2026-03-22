
namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// 技能上下文提供器 - 将可用技能信息注入到 AI 上下文中
/// </summary>
/// <remarks>
/// <para>
/// 此类实现 IContextProvider，用于在 AI 调用前提供可用技能的信息。
/// 支持三种注入模式（InjectionMode）：
/// Instructions - 摘要注入到系统指令；OnDemandTools - 暴露 skill_search/skill_get/skill_activate 工具；Both - 两者都启用。
/// </para>
/// <para>
/// 此类由 AgentExecutorOptionsBuilder 手动构造，不在 DI 容器中注册。
/// </para>
/// </remarks>
public sealed class SkillContextProvider : IContextProvider
{
    private readonly ISkillRegistry _registry;
    private readonly ISkillTemplateEngine _templateEngine;
    private readonly SkillsOptions _options;
    private readonly ISkillLoadTracker? _skillLoadTracker;
    private readonly ILogger<SkillContextProvider> _logger;

    // 按需工具（仅当 InjectionMode 为 OnDemandTools 或 Both 时非空）
    private readonly AITool[] _skillTools;

    // 当前 session 已激活的技能（通过 _activatedLock 保护线程安全）
    private readonly object _activatedLock = new();
    private readonly List<SkillDefinition> _activatedSkills = [];

    /// <summary>
    /// ToolContext.Items 中存储已加载 Skill slug 的 key
    /// </summary>
    internal const string LoadedSkillsKey = "RequiresSkill.LoadedSlugs";

    /// <summary>
    /// 初始化 SkillContextProvider
    /// </summary>
    public SkillContextProvider(
        ISkillRegistry registry,
        ISkillTemplateEngine templateEngine,
        SkillsOptions options,
        ILogger<SkillContextProvider> logger,
        ISkillLoadTracker? skillLoadTracker = null)
    {
        _registry = Check.NotNull(registry);
        _templateEngine = Check.NotNull(templateEngine);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _skillLoadTracker = skillLoadTracker;

        var mode = options.InjectionMode;
        if (mode is SkillInjectionMode.OnDemandTools or SkillInjectionMode.Both)
        {
            _skillTools =
            [
                AIFunctionFactory.Create(
                    SkillSearchAsync,
                    name: "skill_search",
                    description: "Search for available skills by keyword."),
                AIFunctionFactory.Create(
                    SkillGetAsync,
                    name: "skill_get",
                    description: "Get full content of a skill by slug."),
                AIFunctionFactory.Create(
                    SkillActivateAsync,
                    name: "skill_activate",
                    description: "Activate a skill with parameters. Applies constraints and returns rendered content."),
                AIFunctionFactory.Create(
                    SkillDeactivateAsync,
                    name: "skill_deactivate",
                    description: "Deactivate a previously activated skill by slug. Removes its constraints.")
            ];
        }
        else
        {
            _skillTools = [];
        }
    }

    /// <inheritdoc />
    public async Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        try
        {
            var injection = new ContextInjection();
            var mode = _options.InjectionMode;

            // Instructions mode: inject skill summary (NOT full content)
            if (mode is SkillInjectionMode.Instructions or SkillInjectionMode.Both)
            {
                var skills = await _registry.GetAvailableSkillsAsync(ct);
                if (skills.Count > 0)
                {
                    injection.Messages = [new ChatMessage(ChatRole.System, BuildSkillSummary(skills))];
                    _logger.LogDebug("Injected {Count} skill summaries into context as instructions", skills.Count);
                }
            }

            // OnDemand mode: inject tools
            if (_skillTools.Length > 0)
            {
                injection.Tools = [.. _skillTools];
                _logger.LogDebug("Injected {Count} skill tools into context", _skillTools.Length);
            }

            // Include activated skills (thread-safe snapshot)
            lock (_activatedLock)
            {
                if (_activatedSkills.Count > 0)
                    injection.ActiveSkills = [.. _activatedSkills];
            }

            return injection.HasContent ? injection : ContextInjection.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load skills for context injection");
            return ContextInjection.Empty;
        }
    }

    /// <inheritdoc />
    public Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Searches for available skills by keyword.
    /// </summary>
    private async Task<string> SkillSearchAsync(
        [Description("Search keyword")] string keyword,
        CancellationToken ct = default)
    {
        var results = await _registry.SearchAsync(keyword, _options.SearchMaxResults, ct);
        if (results.Count == 0)
            return $"No skills found matching: {keyword}";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} skill(s):");
        foreach (var s in results)
        {
            sb.AppendLine($"- **{s.Name}** (slug: {s.Slug})");
            if (!string.IsNullOrWhiteSpace(s.Description))
                sb.AppendLine($"  {(s.Description.Length > 200 ? s.Description[..200] + "..." : s.Description)}");
        }
        sb.AppendLine("\nUse skill_get or skill_activate with the slug.");
        return sb.ToString();
    }

    /// <summary>
    /// Gets full content of one or more skills by slug (comma-separated for batch loading).
    /// </summary>
    private async Task<string> SkillGetAsync(
        [Description("Skill slug (supports comma-separated for batch loading, e.g. 'sql-query-patterns, data-dictionary')")] string slug,
        CancellationToken ct = default)
    {
        var slugs = slug.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (slugs.Length == 1)
        {
            var skill = await _registry.GetBySlugAsync(slugs[0], ct);
            if (skill == null)
                return $"Skill not found: {slugs[0]}";

            MarkSkillLoaded(slugs[0]);
            var result = _templateEngine.Render(skill);
            return result.Success ? result.RenderedContent : skill.Content;
        }

        // 批量加载多个 Skill
        var sb = new StringBuilder();
        foreach (var s in slugs)
        {
            var skill = await _registry.GetBySlugAsync(s, ct);
            if (skill == null)
            {
                sb.AppendLine($"--- Skill not found: {s} ---\n");
                continue;
            }

            MarkSkillLoaded(s);
            var result = _templateEngine.Render(skill);
            var content = result.Success ? result.RenderedContent : skill.Content;
            sb.AppendLine($"--- {skill.Name} ---\n");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Activates a skill with parameters. Records the skill as activated.
    /// </summary>
    private async Task<string> SkillActivateAsync(
        [Description("Skill slug")] string slug,
        [Description("Parameters as JSON object")] string? parameters = null,
        CancellationToken ct = default)
    {
        var skill = await _registry.GetBySlugAsync(slug, ct);
        if (skill == null)
            return $"Skill not found: {slug}";

        // Parse parameters
        Dictionary<string, string>? paramDict = null;
        if (!string.IsNullOrWhiteSpace(parameters))
        {
            try
            {
                paramDict = JsonSerializer.Deserialize<Dictionary<string, string>>(parameters);
            }
            catch
            {
                return "Invalid parameters JSON format.";
            }
        }

        // 幂等检查：同 slug 已激活时，相同参数直接返回，不同参数更新
        lock (_activatedLock)
        {
            var existing = _activatedSkills.FindIndex(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0 && paramDict == null)
            {
                MarkSkillLoaded(slug);
                return $"Skill '{skill.Name}' is already activated.";
            }
        }

        var renderResult = _templateEngine.Render(skill, paramDict);
        if (!renderResult.Success)
            return $"Skill activation failed:\n{string.Join("\n", renderResult.Errors)}";

        lock (_activatedLock)
        {
            var existing = _activatedSkills.FindIndex(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                _activatedSkills.RemoveAt(existing);
            _activatedSkills.Add(skill);
        }

        // Activation implies the AI has read the skill content — mark as loaded
        // so RequiresSkillToolMiddleware doesn't reject tools requiring this skill
        MarkSkillLoaded(slug);

        var sb = new StringBuilder();
        sb.AppendLine($"## Skill Activated: {skill.Name}");
        sb.AppendLine();
        sb.AppendLine(renderResult.RenderedContent);
        if (skill.AllowedToolGroups is { Count: > 0 })
            sb.AppendLine($"\n**Tool restriction applied**: Only [{string.Join(", ", skill.AllowedToolGroups)}] tools available.");
        if (skill.RequiredModel != null)
            sb.AppendLine($"**Model override**: Using {skill.RequiredModel}.");
        return sb.ToString();
    }

    /// <summary>
    /// Deactivates a previously activated skill. Removes its constraints from the session.
    /// </summary>
    private Task<string> SkillDeactivateAsync(
        [Description("Skill slug to deactivate")] string slug,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return Task.FromResult("Slug is required.");

        int removed;
        lock (_activatedLock)
        {
            removed = _activatedSkills.RemoveAll(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(removed > 0
            ? $"Skill '{slug}' has been deactivated. Its constraints are no longer applied."
            : $"Skill '{slug}' was not active.");
    }

    /// <summary>
    /// Builds a skill summary (name + description + WhenToUse), NOT the full content.
    /// </summary>
    private static string BuildSkillSummary(IReadOnlyList<SkillDefinition> skills)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Available Skills");
        sb.AppendLine();
        foreach (var skill in skills)
        {
            sb.AppendLine($"### {skill.Name} (slug: {skill.Slug})");
            if (!string.IsNullOrWhiteSpace(skill.Description))
                sb.AppendLine(skill.Description);
            if (!string.IsNullOrWhiteSpace(skill.WhenToUse))
            {
                sb.AppendLine();
                sb.AppendLine($"**When to use:** {skill.WhenToUse}");
            }
            if (skill.Parameters.Count > 0)
                sb.AppendLine($"**Parameters:** {string.Join(", ", skill.Parameters.Select(p => p.Required ? p.Name : $"{p.Name}?"))}");
            sb.AppendLine();
        }
        sb.AppendLine("Use skill_activate to activate a skill with parameters.");
        return sb.ToString();
    }

    /// <summary>
    /// 标记 Skill 已通过 skill_get 加载。
    /// Writes to ISkillLoadTracker (scoped, persists across tool calls) as primary store,
    /// and also writes to ToolContext.Items for backward compatibility.
    /// </summary>
    internal void MarkSkillLoaded(string slug)
    {
        // Primary: scoped tracker persists across tool calls within the same HTTP request
        _skillLoadTracker?.MarkLoaded(slug);

        // Backward compatibility: also write to ToolContext.Items (per-tool-call, transient)
        var toolContext = ToolContextAccessor.Current;
        if (toolContext == null) return;

        var loaded = GetOrCreateLoadedSlugs(toolContext);
        loaded.TryAdd(slug, 0);
    }

    /// <summary>
    /// 获取已加载的 Skill slug 集合（线程安全）
    /// </summary>
    internal static ConcurrentDictionary<string, byte> GetOrCreateLoadedSlugs(IToolContext toolContext)
    {
        if (toolContext.Items.TryGetValue(LoadedSkillsKey, out var obj) && obj is ConcurrentDictionary<string, byte> existing)
            return existing;

        var loaded = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        toolContext.Items[LoadedSkillsKey] = loaded;
        return loaded;
    }
}
