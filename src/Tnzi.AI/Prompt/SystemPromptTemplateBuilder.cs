namespace Tnzi.AI.Prompt;

/// <summary>
/// 统一系统提示词构建器 — 组装 13 个 XML 标签段落为完整的 system prompt
/// </summary>
/// <remarks>
/// <para>
/// 13 个标准段落（按默认顺序）：
/// soul(0), user_profile(10), memory(20), instructions(30), skill_system(40),
/// available-deferred-tools(50), clarification_system(60), sub_agent_orchestration(70),
/// working_directory(80), response_style(90), citations(100), critical_reminders(110), current_date(120)
/// </para>
/// </remarks>
public class SystemPromptTemplateBuilder
{
    private readonly Dictionary<string, SystemPromptSection> _sections = new(StringComparer.Ordinal);
    private readonly List<ISystemPromptSectionProvider> _providers = [];

    /// <summary>标准段落 tag 常量</summary>
    public static class Tags
    {
        public const string Soul = "soul";
        public const string UserProfile = "user_profile";
        public const string Memory = "memory";
        public const string Instructions = "instructions";
        public const string SkillSystem = "skill_system";
        public const string DeferredTools = "available-deferred-tools";
        public const string Clarification = "clarification_system";
        public const string SubAgentOrchestration = "sub_agent_orchestration";
        public const string WorkingDirectory = "working_directory";
        public const string ResponseStyle = "response_style";
        public const string Citations = "citations";
        public const string CriticalReminders = "critical_reminders";
        public const string CurrentDate = "current_date";
    }

    /// <summary>
    /// 添加静态段落（重复 tag 覆盖先前值）
    /// </summary>
    public SystemPromptTemplateBuilder AddSection(string tag, string? content, int order)
    {
        Check.NotNullOrWhiteSpace(tag);
        _sections[tag] = new SystemPromptSection(tag, content, order);
        return this;
    }

    /// <summary>
    /// 添加动态段落提供器（Build 时求值）
    /// </summary>
    public SystemPromptTemplateBuilder AddSectionProvider(ISystemPromptSectionProvider provider)
    {
        Check.NotNull(provider);
        _providers.Add(provider);
        return this;
    }

    /// <summary>
    /// 移除指定 tag 的段落
    /// </summary>
    public SystemPromptTemplateBuilder RemoveSection(string tag)
    {
        _sections.Remove(tag);
        return this;
    }

    /// <summary>
    /// 异步构建完整的系统提示词
    /// </summary>
    public async Task<string> BuildAsync(CancellationToken cancellationToken = default)
    {
        var merged = new Dictionary<string, SystemPromptSection>(_sections, StringComparer.Ordinal);

        foreach (var provider in _providers)
        {
            var section = await provider.GetSectionAsync(cancellationToken);
            if (section != null)
            {
                merged[section.Tag] = section;
            }
        }

        return Assemble(merged);
    }

    private static string Assemble(Dictionary<string, SystemPromptSection> merged)
    {
        var ordered = merged.Values
            .Where(s => !string.IsNullOrEmpty(s.Content))
            .OrderBy(s => s.Order)
            .ToList();

        if (ordered.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (i > 0) sb.Append("\n\n");
            var section = ordered[i];
            sb.Append('<').Append(section.Tag).Append(">\n");
            sb.Append(section.Content);
            sb.Append("\n</").Append(section.Tag).Append('>');
        }

        return sb.ToString();
    }
}
