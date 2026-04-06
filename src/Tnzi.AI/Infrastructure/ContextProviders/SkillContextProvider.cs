
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
    private readonly ISkillConstraintEnforcer? _constraintEnforcer;
    private readonly ISkillLoadTracker? _skillLoadTracker;
    private readonly ILogger<SkillContextProvider> _logger;
    private readonly string? _agentName;

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
        ISkillConstraintEnforcer? constraintEnforcer = null,
        ISkillLoadTracker? skillLoadTracker = null,
        string? agentName = null)
    {
        _registry = Check.NotNull(registry);
        _templateEngine = Check.NotNull(templateEngine);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _constraintEnforcer = constraintEnforcer;
        _skillLoadTracker = skillLoadTracker;
        _agentName = agentName;

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
                    description: "Deactivate a previously activated skill by slug. Removes its constraints."),
                AIFunctionFactory.Create(
                    SkillGetResourceAsync,
                    name: "skill_get_resource",
                    description: "Get a skill's attached resource file (scripts, templates, references). Returns file content.")
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

            // Instructions mode: inject skill summary (name + description only)
            if (mode is SkillInjectionMode.Instructions or SkillInjectionMode.Both)
            {
                var allSkills = await _registry.GetAvailableSkillsAsync(ct);
                var skills = FilterByAgent(allSkills);
                if (skills.Count > 0)
                {
                    injection.Messages = [new ChatMessage(ChatRole.System, BuildSkillSummary(skills))];
                    _logger.LogDebug("Injected {Count} skill summaries into context for agent '{Agent}'", skills.Count, _agentName ?? "(all)");
                }
            }

            // OnDemand mode: inject tools
            if (_skillTools.Length > 0)
            {
                injection.Tools = [.. _skillTools];
                _logger.LogDebug("Injected {Count} skill tools into context", _skillTools.Length);
            }

            // Include activated skills (thread-safe snapshot)
            List<SkillDefinition>? activeSnapshot = null;
            lock (_activatedLock)
            {
                if (_activatedSkills.Count > 0)
                {
                    activeSnapshot = [.. _activatedSkills];
                    injection.ActiveSkills = activeSnapshot;
                }
            }

            // 将已激活的 Skill 包装为 AIFunction，使 LLM 可原生调用
            if (activeSnapshot is { Count: > 0 })
            {
                injection.Tools ??= [];
                foreach (var activeSkill in activeSnapshot)
                {
                    injection.Tools.Add(new SkillAIFunction(activeSkill, _templateEngine, _constraintEnforcer));
                }
                _logger.LogDebug("Injected {Count} activated skills as AIFunction tools", activeSnapshot.Count);
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
        var allResults = await _registry.SearchAsync(keyword, _options.SearchMaxResults, ct);
        var results = FilterByAgent(allResults);
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
            var content = result.Success ? result.RenderedContent : skill.Content;
            return AppendResourceHint(skill, content);
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
    /// Gets a specific resource file from a skill (scripts, templates, references).
    /// </summary>
    private async Task<string> SkillGetResourceAsync(
        [Description("Skill slug")] string slug,
        [Description("Resource path (e.g. 'scripts/generate.py', 'templates/SOUL.template.md')")] string path,
        CancellationToken ct = default)
    {
        var skill = await _registry.GetBySlugAsync(slug, ct);
        if (skill == null)
            return $"Skill not found: {slug}";

        if (skill.Resources.Count == 0)
            return $"Skill '{slug}' has no attached resources.";

        // 精确匹配
        if (skill.Resources.TryGetValue(path, out var content))
            return content;

        // 模糊匹配（文件名）
        var fileName = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
        var match = skill.Resources.FirstOrDefault(r =>
            r.Key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Key, fileName, StringComparison.OrdinalIgnoreCase));

        if (match.Key != null)
            return match.Value;

        var available = string.Join("\n", skill.Resources.Keys.Select(k => $"  - {k}"));
        return $"Resource not found: '{path}' in skill '{slug}'.\n\nAvailable resources:\n{available}";
    }

    /// <summary>
    /// 在 skill_get 返回内容末尾附加可用资源列表提示（如果有附属资源）
    /// </summary>
    private static string AppendResourceHint(SkillDefinition skill, string content)
    {
        if (skill.Resources.Count == 0)
            return content;

        var sb = new StringBuilder(content);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"This skill has {skill.Resources.Count} attached resource(s):");
        foreach (var key in skill.Resources.Keys)
            sb.AppendLine($"  - /mnt/skills/{skill.Slug}/{key}");
        sb.AppendLine();
        sb.AppendLine($"Access via sandbox: `read_file(\"/mnt/skills/{skill.Slug}/path\")` or `bash(\"python /mnt/skills/{skill.Slug}/scripts/script.py\")`");
        sb.AppendLine($"Fallback (no sandbox): `skill_get_resource(\"{skill.Slug}\", \"path\")`");

        return sb.ToString();
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
    /// 按 Agent 名称过滤技能列表（支持通配符匹配）。
    /// </summary>
    private IReadOnlyList<SkillDefinition> FilterByAgent(IReadOnlyList<SkillDefinition> skills)
    {
        // 内部技能不暴露给 Agent（仅作为共享资源依赖存在）
        // 无 agent 上下文时，只返回无 agents 限制的 skill
        if (string.IsNullOrWhiteSpace(_agentName))
            return skills.Where(s => !s.IsInternal && s.Agents is not { Count: > 0 }).ToList();

        return skills.Where(s => !s.IsInternal && IsAgentAllowed(s, _agentName)).ToList();
    }

    /// <summary>
    /// 检查资源路径列表是否匹配技能的 Paths glob 模式。
    /// </summary>
    /// <param name="skill">技能定义（含 Paths glob 模式列表）</param>
    /// <param name="resourcePaths">当前上下文的资源路径列表</param>
    /// <returns>任意路径匹配任意模式时返回 true</returns>
    public static bool MatchesResourcePaths(SkillDefinition skill, IReadOnlyList<string> resourcePaths)
    {
        if (skill.Paths.Count == 0) return false;

        foreach (var resourcePath in resourcePaths)
        {
            foreach (var pattern in skill.Paths)
            {
                if (GlobMatch(resourcePath, pattern))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 简易 glob 匹配：支持 * (单层段内任意字符) 和 ** (递归匹配任意层)。
    /// 路径分隔符统一为 /，匹配不区分大小写。
    /// </summary>
    public static bool GlobMatch(string path, string pattern)
    {
        // 统一分隔符
        var normalizedPath = path.Replace('\\', '/');
        var normalizedPattern = pattern.Replace('\\', '/');

        // 精确匹配（忽略大小写）
        if (normalizedPath.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase))
            return true;

        var pathSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var patternSegments = normalizedPattern.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return GlobMatchSegments(pathSegments, 0, patternSegments, 0);
    }

    /// <summary>
    /// 递归段匹配：pi = pathIndex, gi = patternIndex
    /// </summary>
    private static bool GlobMatchSegments(string[] path, int pi, string[] pattern, int gi)
    {
        // 两边都消耗完 → 匹配成功
        if (gi == pattern.Length)
            return pi == path.Length;

        // ** 递归段
        if (pattern[gi] == "**")
        {
            // ** 可以匹配零个到多个段
            for (var skip = 0; skip <= path.Length - pi; skip++)
            {
                if (GlobMatchSegments(path, pi + skip, pattern, gi + 1))
                    return true;
            }
            return false;
        }

        // 路径段已用完但模式还有 → 不匹配
        if (pi == path.Length)
            return false;

        // 单段通配符匹配（* 匹配段内任意字符）
        if (WildcardMatchSegment(path[pi], pattern[gi]))
            return GlobMatchSegments(path, pi + 1, pattern, gi + 1);

        return false;
    }

    /// <summary>
    /// 单段内 * 通配符匹配（* 匹配任意字符序列，不含路径分隔符）
    /// </summary>
    private static bool WildcardMatchSegment(string text, string pattern)
    {
        int ti = 0, pi = 0;
        int starTi = -1, starPi = -1;

        while (ti < text.Length)
        {
            if (pi < pattern.Length && pattern[pi] == '*')
            {
                starPi = pi++;
                starTi = ti;
            }
            else if (pi < pattern.Length && char.ToLowerInvariant(text[ti]) == char.ToLowerInvariant(pattern[pi]))
            {
                ti++;
                pi++;
            }
            else if (starPi >= 0)
            {
                pi = starPi + 1;
                ti = ++starTi;
            }
            else
            {
                return false;
            }
        }

        // 跳过剩余的 *
        while (pi < pattern.Length && pattern[pi] == '*')
            pi++;

        return pi == pattern.Length;
    }

    /// <summary>
    /// 检查 Agent 名称是否匹配 agents 列表（支持通配符 *）。
    /// </summary>
    /// <remarks>
    /// Wildcard pattern: "rpi-*" matches "rpi-assistant", "rpi-finance", etc.
    /// </remarks>
    public static bool IsAgentAllowed(SkillDefinition skill, string? agentName)
    {
        // 无 agents 限制 = 所有 agent 可见
        if (skill.Agents is not { Count: > 0 })
            return true;

        // 无 agent 上下文（匿名调用）= 仅显示无限制的 skill
        if (string.IsNullOrWhiteSpace(agentName))
            return false;

        foreach (var pattern in skill.Agents)
        {
            if (pattern.EndsWith('*'))
            {
                // 通配符前缀匹配: rpi-* matches rpi-assistant, rpi-finance
                var prefix = pattern[..^1];
                if (agentName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (pattern.Equals(agentName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Maximum character budget for skill listing in system prompt (default 8000).
    /// When total exceeds budget, skills are truncated by priority (lower priority dropped first).
    /// </summary>
    public const int SkillListingBudget = 8000;

    /// <summary>
    /// Builds a skill summary (name + description only) for system prompt injection.
    /// Applies character budget: if total exceeds <see cref="SkillListingBudget"/>, truncates by priority (lower priority dropped first).
    /// </summary>
    public static string BuildSkillSummary(IReadOnlyList<SkillDefinition> skills)
    {
        const string header = "## Available Skills\n\n";
        const string footer = "\nUse `skill_get` to load a skill's full content, or `skill_activate` to activate with parameters.\n";

        // Sort by priority descending so higher-priority skills are kept first
        var sorted = skills.OrderByDescending(s => s.Priority).ToList();

        var sb = new StringBuilder();
        sb.Append(header);

        var budgetRemaining = SkillListingBudget - header.Length - footer.Length;
        var includedCount = 0;
        var totalCount = sorted.Count;

        foreach (var skill in sorted)
        {
            var line = new StringBuilder();
            line.Append($"- **{skill.Name}** (`{skill.Slug}`)");
            if (!string.IsNullOrWhiteSpace(skill.Description))
                line.Append($" — {skill.Description}");
            line.AppendLine();

            var lineStr = line.ToString();
            if (lineStr.Length > budgetRemaining && includedCount > 0)
            {
                // Budget exceeded — append truncation notice
                var truncated = totalCount - includedCount;
                sb.AppendLine($"- _...and {truncated} more skill(s) omitted due to context budget._");
                break;
            }

            sb.Append(lineStr);
            budgetRemaining -= lineStr.Length;
            includedCount++;
        }

        sb.Append(footer);
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
