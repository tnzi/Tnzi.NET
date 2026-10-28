namespace Tnzi.AI.Skills;

/// <summary>
/// 技能注册表 - 合并双源、三层优先级、搜索
/// </summary>
public interface ISkillRegistry
{
    /// <summary>获取当前上下文可见的所有技能</summary>
    Task<IReadOnlyList<SkillDefinition>> GetAvailableSkillsAsync(CancellationToken ct = default);

    /// <summary>按 slug 获取（支持 scope:slug 消歧）</summary>
    Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>搜索技能</summary>
    Task<IReadOnlyList<SkillDefinition>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default);

    /// <summary>清除缓存（含文件技能：下一次读取会重新扫描磁盘与内置资源）</summary>
    void InvalidateCache();

    /// <summary>
    /// 数据库里的技能被写过之后调用：只丢掉本作用域已合并、已从数据库读过的结果，不重新扫描文件技能。
    /// 默认实现退回 <see cref="InvalidateCache"/>。
    /// </summary>
    void InvalidateDatabaseSkills() => InvalidateCache();
}
