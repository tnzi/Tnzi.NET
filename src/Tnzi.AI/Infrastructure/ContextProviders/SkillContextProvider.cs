
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
    private readonly ILogger<SkillContextProvider> _logger;

    // 按需工具（仅当 InjectionMode 为 OnDemandTools 或 Both 时非空）
    private readonly AITool[] _skillTools;

    // 当前 session 已激活的技能
    private readonly List<SkillDefinition> _activatedSkills = [];

    /// <summary>
    /// 初始化 SkillContextProvider
    /// </summary>
    public SkillContextProvider(
        ISkillRegistry registry,
        ISkillTemplateEngine templateEngine,
        SkillsOptions options,
        ILogger<SkillContextProvider> logger)
    {
        _registry = Check.NotNull(registry);
        _templateEngine = Check.NotNull(templateEngine);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);

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
                    description: "Activate a skill with parameters. Applies constraints and returns rendered content.")
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

            // Include activated skills
            if (_activatedSkills.Count > 0)
                injection.ActiveSkills = [.. _activatedSkills];

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
        var results = await _registry.SearchAsync(keyword, 10, ct);
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
    /// Gets full content of a skill by slug.
    /// </summary>
    private async Task<string> SkillGetAsync(
        [Description("Skill slug")] string slug,
        CancellationToken ct = default)
    {
        var skill = await _registry.GetBySlugAsync(slug, ct);
        if (skill == null)
            return $"Skill not found: {slug}";

        // Render with default values (preview)
        var result = _templateEngine.Render(skill);
        return result.Success ? result.RenderedContent : skill.Content;
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

        // Idempotency check
        if (_activatedSkills.Any(s => s.Slug == slug))
            return $"Skill '{skill.Name}' is already activated.";

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

        var renderResult = _templateEngine.Render(skill, paramDict);
        if (!renderResult.Success)
            return $"Skill activation failed:\n{string.Join("\n", renderResult.Errors)}";

        _activatedSkills.Add(skill);

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
}
