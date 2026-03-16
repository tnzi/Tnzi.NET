namespace Tnzi.AI.Skills;

/// <summary>
/// 技能注册表 — 合并双源、三层优先级、搜索
/// </summary>
public interface ISkillRegistry
{
    /// <summary>获取当前上下文可见的所有技能</summary>
    Task<IReadOnlyList<SkillDefinition>> GetAvailableSkillsAsync(CancellationToken ct = default);

    /// <summary>按 slug 获取（支持 scope:slug 消歧）</summary>
    Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>搜索技能</summary>
    Task<IReadOnlyList<SkillDefinition>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default);

    /// <summary>清除缓存</summary>
    void InvalidateCache();
}
