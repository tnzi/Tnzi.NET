namespace Tnzi.AI.Skills;

/// <summary>
/// 技能存储接口（只读）— 从数据源加载技能。
/// 写操作（CRUD）由 ISkillService 通过 IRepository 直接处理。
/// </summary>
public interface ISkillStore
{
    /// <summary>获取所有技能</summary>
    Task<List<SkillDefinition>> GetAllAsync(CancellationToken ct = default);

    /// <summary>按 slug 获取技能</summary>
    Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default);
}
