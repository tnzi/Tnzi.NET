
namespace Tnzi.AI.Tools;

/// <summary>
/// 技能按需工具提供者 - 暴露 skill_search / skill_get 供模型按需调用
/// </summary>
/// <remarks>
/// 当 Skills.InjectionMode 为 OnDemandTools 或 Both 时，由 SkillContextProvider 将此类方法转为 AITool 注入上下文。
/// </remarks>
public sealed class SkillToolsProvider
{
    private readonly SkillLoader _skillLoader;
    private readonly ILogger<SkillToolsProvider> _logger;

    /// <summary>
    /// 初始化 SkillToolsProvider
    /// </summary>
    public SkillToolsProvider(SkillLoader skillLoader, ILogger<SkillToolsProvider> logger)
    {
        _skillLoader = Check.NotNull(skillLoader);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 按关键词搜索可用技能
    /// </summary>
    /// <param name="keyword">搜索关键词（匹配技能名称、描述、WhenToUse）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的技能摘要（名称、ID、简短描述），无匹配时返回说明文本</returns>
    [Description("Search for available skills by keyword. Returns skill names, IDs, and short descriptions.")]
    public async Task<string> SkillSearchAsync(
        [Description("Search keyword to match skill name, description, or when-to-use")] string keyword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return "Please provide a non-empty keyword for skill search.";
        }

        try
        {
            var allSkills = await _skillLoader.LoadSkillsAsync(cancellationToken);
            var validSkills = new List<SkillDefinition>();

            foreach (var skill in allSkills)
            {
                if (!skill.Enabled)
                {
                    continue;
                }

                var validation = _skillLoader.ValidateRequirements(skill);
                if (!validation.IsValid)
                {
                    continue;
                }

                var k = keyword.Trim();
                var match = (skill.Name?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (skill.Description?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (skill.WhenToUse?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (skill.Id?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false);

                if (match)
                {
                    validSkills.Add(skill);
                }
            }

            if (validSkills.Count == 0)
            {
                return $"No skills found matching keyword: {keyword}.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {validSkills.Count} skill(s) matching \"{keyword}\":");
            sb.AppendLine();

            foreach (var skill in validSkills.OrderByDescending(s => s.Priority))
            {
                sb.AppendLine($"- **{skill.Name}** (ID: {skill.Id})");
                if (!string.IsNullOrWhiteSpace(skill.Description))
                {
                    var desc = skill.Description.Length > 200 ? skill.Description[..200] + "..." : skill.Description;
                    sb.AppendLine($"  {desc}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("Use skill_get with the skill ID to retrieve full content.");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skill search failed for keyword: {Keyword}", keyword);
            return $"Skill search failed: {ex.Message}";
        }
    }

    /// <summary>
    /// 根据技能 ID 获取技能完整内容
    /// </summary>
    /// <param name="skillId">技能 ID（由 skill_search 返回）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>技能完整内容（SKILL.md 正文），未找到时返回错误说明</returns>
    [Description("Get full content of a skill by ID. Use skill_search first to find skill IDs.")]
    public async Task<string> SkillGetAsync(
        [Description("Skill ID returned by skill_search")] string skillId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return "Please provide a non-empty skill ID. Use skill_search to find available skill IDs.";
        }

        try
        {
            var allSkills = await _skillLoader.LoadSkillsAsync(cancellationToken);

            var skill = allSkills.FirstOrDefault(s =>
                s.Enabled
                && string.Equals(s.Id, skillId.Trim(), StringComparison.OrdinalIgnoreCase));

            if (skill == null)
            {
                return $"No skill found with ID: {skillId}. Use skill_search to list available skills.";
            }

            var validation = _skillLoader.ValidateRequirements(skill);
            if (!validation.IsValid)
            {
                return $"Skill \"{skill.Name}\" (ID: {skill.Id}) requirements not met: {validation.GetFailureReason()}. Cannot return content.";
            }

            _logger.LogDebug("Returning full content for skill: {SkillName} ({SkillId})", skill.Name, skill.Id);

            return skill.Content;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skill get failed for ID: {SkillId}", skillId);
            return $"Skill get failed: {ex.Message}";
        }
    }
}