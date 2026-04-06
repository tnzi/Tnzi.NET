namespace Tnzi.AI.Skills;

/// <summary>
/// 空操作技能加载跟踪器 - 允许核心模块在未加载 Skills 子模块时正常运行
/// </summary>
public class NoOpSkillLoadTracker : ISkillLoadTracker, INoOpService
{
    private readonly HashSet<string> _loadedSlugs = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoaded(string slug)
    {
        return !string.IsNullOrWhiteSpace(slug) && _loadedSlugs.Contains(slug);
    }

    public void MarkLoaded(string slug)
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            _loadedSlugs.Add(slug);
        }
    }

    public IReadOnlyCollection<string> LoadedSlugs => _loadedSlugs;
}
