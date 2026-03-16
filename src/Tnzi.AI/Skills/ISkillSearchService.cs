namespace Tnzi.AI.Skills;

/// <summary>
/// 技能搜索服务 — 关键词搜索 + 语义降级
/// </summary>
public interface ISkillSearchService
{
    /// <summary>搜索技能</summary>
    Task<IReadOnlyList<SkillDefinition>> SearchAsync(
        IReadOnlyList<SkillDefinition> candidates,
        string query,
        int maxResults,
        CancellationToken ct = default);
}
