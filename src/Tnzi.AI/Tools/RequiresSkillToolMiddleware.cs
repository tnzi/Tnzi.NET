namespace Tnzi.AI.Tools;

/// <summary>
/// 工具执行中间件 — [RequiresSkill] 兜底检查。
/// 若工具标记了 [RequiresSkill] 但 AI 未通过 skill_get 加载对应技能，
/// 拒绝执行并返回技能内容，引导 AI 阅读后重试。
/// </summary>
public class RequiresSkillToolMiddleware : IToolExecutionMiddleware
{
    private readonly IToolRegistry _toolRegistry;
    private readonly ISkillLoadTracker _skillLoadTracker;
    private readonly ISkillRegistry? _skillRegistry;
    private readonly ISkillTemplateEngine? _templateEngine;
    private readonly ILogger<RequiresSkillToolMiddleware> _logger;

    // 缓存 toolName → RequiresSkillSlugs 映射（线程安全懒初始化，构建后不变）
    private volatile Dictionary<string, IReadOnlyList<string>>? _toolSkillMap;

    public RequiresSkillToolMiddleware(
        IToolRegistry toolRegistry,
        ISkillLoadTracker skillLoadTracker,
        ILogger<RequiresSkillToolMiddleware> logger,
        ISkillRegistry? skillRegistry = null,
        ISkillTemplateEngine? templateEngine = null)
    {
        _toolRegistry = Check.NotNull(toolRegistry);
        _skillLoadTracker = Check.NotNull(skillLoadTracker);
        _logger = Check.NotNull(logger);
        _skillRegistry = skillRegistry;
        _templateEngine = templateEngine;
    }

    public async Task<object?> InvokeAsync(ToolExecutionContext context, Func<Task<object?>> next)
    {
        var requiredSlugs = GetRequiredSlugs(context.ToolName);
        if (requiredSlugs == null || requiredSlugs.Count == 0)
            return await next();

        // 检查哪些 Skill 尚未加载（通过 scoped tracker，跨工具调用共享状态）
        var missingSlugs = requiredSlugs
            .Where(s => !_skillLoadTracker.IsLoaded(s))
            .ToList();

        if (missingSlugs.Count == 0)
            return await next();

        // 有未加载的 Skill → 拒绝执行，返回 Skill 内容
        _logger.LogDebug(
            "Tool '{ToolName}' requires skills [{MissingSlugs}] which are not yet loaded. Returning skill content.",
            context.ToolName, string.Join(", ", missingSlugs));

        string content;
        try
        {
            content = await BuildSkillContentAsync(missingSlugs);
        }
        catch (Exception ex)
        {
            // Skill 加载出错不应阻塞工具执行 — 标记 loaded 并放行
            _logger.LogError(ex, "Failed to load skill content for [{Slugs}]. Allowing tool execution without guidelines",
                string.Join(", ", missingSlugs));
            foreach (var slug in missingSlugs)
                _skillLoadTracker.MarkLoaded(slug);
            return await next();
        }

        // 标记为已加载（AI 收到内容后等同于已阅读）
        foreach (var slug in missingSlugs)
        {
            _skillLoadTracker.MarkLoaded(slug);
        }

        return $"⚠️ Required guidelines loaded. You may now call this tool again — do NOT call skill_get, just retry the tool directly.\n\n{content}";
    }

    private IReadOnlyList<string>? GetRequiredSlugs(string toolName)
    {
        _toolSkillMap ??= BuildToolSkillMap();
        return _toolSkillMap.GetValueOrDefault(toolName);
    }

    private Dictionary<string, IReadOnlyList<string>> BuildToolSkillMap()
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in _toolRegistry.GetAllTools())
        {
            if (tool.RequiresSkillSlugs is { Count: > 0 })
                map[tool.Name] = tool.RequiresSkillSlugs;
        }
        return map;
    }

    private async Task<string> BuildSkillContentAsync(List<string> slugs)
    {
        if (_skillRegistry == null || _templateEngine == null)
        {
            _logger.LogWarning("ISkillRegistry or ISkillTemplateEngine not available — cannot load skill content for [{Slugs}]",
                string.Join(", ", slugs));
            return $"Required skills [{string.Join(", ", slugs)}] could not be loaded (skill system not available). Proceeding without guidelines.";
        }

        var sb = new StringBuilder();
        foreach (var slug in slugs)
        {
            var skill = await _skillRegistry.GetBySlugAsync(slug, CancellationToken.None);
            if (skill == null)
            {
                _logger.LogWarning(
                    "Required skill '{Slug}' not found — tool will proceed without guidelines. " +
                    "Check [RequiresSkill] attribute for typos or ensure the skill file exists", slug);
                sb.AppendLine($"--- Skill '{slug}' not found (proceeding without guidelines) ---\n");
                continue;
            }

            var result = _templateEngine.Render(skill);
            var content = result.Success ? result.RenderedContent : skill.Content;
            sb.AppendLine($"--- {skill.Name} ---\n");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
