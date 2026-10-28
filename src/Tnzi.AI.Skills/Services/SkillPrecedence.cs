namespace Tnzi.AI.Skills.Services;

/// <summary>
/// 同一个 slug 有多份技能时谁胜出：作用域 System &gt; Tenant &gt; User，同作用域内 <see cref="SkillDefinition.Priority"/> 越大越优先，
/// 仍然相同时先出现的保留。
/// </summary>
/// <remarks>
/// 列表（<see cref="SkillRegistry.GetAvailableSkillsAsync"/>）与按 slug 查找（<see cref="SkillRegistry.GetBySlugAsync"/> /
/// <see cref="FileSystemSkillStore.GetBySlugAsync"/>）必须用同一把尺子：此前查找取的是「第一个加载到的」，
/// 列表里显示的与 <c>skill_get</c> / <c>skill_activate</c> 真正拿到的可以是两份不同的技能。
/// </remarks>
internal static class SkillPrecedence
{
    /// <summary><paramref name="candidate"/> 是否应取代已选中的 <paramref name="current"/>。</summary>
    public static bool Beats(SkillDefinition candidate, SkillDefinition current)
        => candidate.Scope < current.Scope
           || (candidate.Scope == current.Scope && candidate.Priority > current.Priority);

    /// <summary>从同 slug 的候选里选出胜者；没有候选返回 null。</summary>
    public static SkillDefinition? PickWinner(IEnumerable<SkillDefinition> candidates)
    {
        SkillDefinition? winner = null;
        foreach (var candidate in candidates)
        {
            if (winner == null || Beats(candidate, winner))
                winner = candidate;
        }

        return winner;
    }
}
