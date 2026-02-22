
namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// 技能上下文提供器 - 将可用技能信息注入到 AI 上下文中
/// </summary>
/// <remarks>
/// <para>
/// 此类实现 IContextProvider，用于在 AI 调用前提供可用技能的信息。
/// 支持三种注入模式（InjectionMode）：
/// Instructions - 全量注入到系统指令；OnDemandTools - 暴露 skill_search/skill_get 工具；Both - 两者都启用。
/// </para>
/// </remarks>
public sealed class SkillContextProvider : IContextProvider
{
    private readonly SkillLoader _skillLoader;
    private readonly SkillsOptions _options;
    private readonly ILogger<SkillContextProvider> _logger;
    private readonly SkillToolsProvider? _skillToolsProvider;

    // 按需工具（仅当 InjectionMode 为 OnDemandTools 或 Both 时非空）
    private readonly AITool[] _skillTools;

    // 缓存加载的技能
    private List<SkillDefinition>? _cachedSkills;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    /// <summary>
    /// 初始化 SkillContextProvider（仅 Instructions 模式，无按需工具）
    /// </summary>
    public SkillContextProvider(
        SkillLoader skillLoader,
        SkillsOptions options,
        ILogger<SkillContextProvider> logger)
        : this(skillLoader, options, logger, null)
    {
    }

    /// <summary>
    /// 初始化 SkillContextProvider（支持按需工具时传入 SkillToolsProvider）
    /// </summary>
    public SkillContextProvider(
        SkillLoader skillLoader,
        SkillsOptions options,
        ILogger<SkillContextProvider> logger,
        SkillToolsProvider? skillToolsProvider)
    {
        _skillLoader = Check.NotNull(skillLoader);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _skillToolsProvider = skillToolsProvider;

        if ((options.InjectionMode == SkillInjectionMode.OnDemandTools || options.InjectionMode == SkillInjectionMode.Both)
            && skillToolsProvider != null)
        {
            _skillTools =
            [
                AIFunctionFactory.Create(
                    SkillSearchAsync,
                    name: "skill_search",
                    description: "Search for available skills by keyword. Returns skill names, IDs, and short descriptions."),
                AIFunctionFactory.Create(
                    SkillGetAsync,
                    name: "skill_get",
                    description: "Get full content of a skill by ID. Use skill_search first to find skill IDs.")
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
        var mode = _options.InjectionMode;
        var instructions = string.Empty;
        List<AITool>? tools = null;

        try
        {
            // 需要注入 Instructions 时（Instructions 或 Both）
            if (mode == SkillInjectionMode.Instructions || mode == SkillInjectionMode.Both)
            {
                var skills = await GetValidSkillsAsync(ct);

                if (skills.Count > 0)
                {
                    instructions = BuildSkillInstructions(skills);
                    _logger.LogDebug("Injected {Count} skills into context as instructions", skills.Count);
                }
            }

            // 需要注入按需工具时（OnDemandTools 或 Both）
            if (_skillTools.Length > 0)
            {
                tools = [.. _skillTools];
                _logger.LogDebug("Injected {Count} skill tools into context", _skillTools.Length);
            }

            if (string.IsNullOrWhiteSpace(instructions) && (tools == null || tools.Count == 0))
            {
                return ContextInjection.Empty;
            }

            var injection = new ContextInjection { Tools = tools };

            if (!string.IsNullOrWhiteSpace(instructions))
            {
                injection.Messages = [new ChatMessage(ChatRole.System, instructions)];
            }

            return injection;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load skills for context injection");
            return ContextInjection.Empty;
        }
    }

    /// <summary>
    /// 供模型按需调用的技能搜索方法（委托给 SkillToolsProvider）
    /// </summary>
    private Task<string> SkillSearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        if (_skillToolsProvider == null)
        {
            return Task.FromResult("Skill search is not available.");
        }

        return _skillToolsProvider.SkillSearchAsync(keyword, cancellationToken);
    }

    /// <summary>
    /// 供模型按需调用的技能获取方法（委托给 SkillToolsProvider）
    /// </summary>
    private Task<string> SkillGetAsync(string skillId, CancellationToken cancellationToken = default)
    {
        if (_skillToolsProvider == null)
        {
            return Task.FromResult("Skill get is not available.");
        }

        return _skillToolsProvider.SkillGetAsync(skillId, cancellationToken);
    }

    /// <inheritdoc />
    public Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        // 技能提供器不需要在调用后处理
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取有效的技能列表
    /// </summary>
    private async Task<List<SkillDefinition>> GetValidSkillsAsync(CancellationToken ct)
    {
        // 使用缓存
        if (_cachedSkills != null)
        {
            return _cachedSkills;
        }

        await _loadLock.WaitAsync(ct);
        try
        {
            // 双重检查
            if (_cachedSkills != null)
            {
                return _cachedSkills;
            }

            // 加载所有技能
            var allSkills = await _skillLoader.LoadSkillsAsync(ct);

            // 验证并过滤
            var validSkills = new List<SkillDefinition>();
            foreach (var skill in allSkills)
            {
                if (!skill.Enabled)
                {
                    continue;
                }

                var validation = _skillLoader.ValidateRequirements(skill);
                if (validation.IsValid)
                {
                    validSkills.Add(skill);
                }
                else
                {
                    _logger.LogDebug(
                        "Skill '{SkillName}' validation failed: {Reason}",
                        skill.Name, validation.GetFailureReason());
                }
            }

            // 按优先级排序
            validSkills = validSkills.OrderByDescending(s => s.Priority).ToList();

            _cachedSkills = validSkills;
            return validSkills;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// 构建技能指令
    /// </summary>
    private static string BuildSkillInstructions(List<SkillDefinition> skills)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Available Skills");
        sb.AppendLine();
        sb.AppendLine("The following skills are available to help you handle user requests:");
        sb.AppendLine();

        foreach (var skill in skills)
        {
            sb.AppendLine($"### {skill.Name}");

            if (!string.IsNullOrWhiteSpace(skill.Description))
            {
                sb.AppendLine(skill.Description);
            }

            if (!string.IsNullOrWhiteSpace(skill.WhenToUse))
            {
                sb.AppendLine();
                sb.AppendLine("**When to use:**");
                sb.AppendLine(skill.WhenToUse);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 清除缓存（用于重新加载技能）
    /// </summary>
    public void ClearCache()
    {
        _cachedSkills = null;
    }
}
