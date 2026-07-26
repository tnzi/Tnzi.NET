namespace Tnzi.AI.Skills;

/// <summary>
/// Scoped implementation - one instance per HTTP request, persists across tool calls.
/// Uses ConcurrentDictionary for thread safety during parallel tool execution.
/// </summary>
public class SkillLoadTracker : ISkillLoadTracker
{
    private readonly ConcurrentDictionary<string, byte> _loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool IsLoaded(string slug) => _loaded.ContainsKey(slug);

    /// <inheritdoc />
    public void MarkLoaded(string slug) => _loaded.TryAdd(slug, 0);

    /// <inheritdoc />
    public IReadOnlyCollection<string> LoadedSlugs => [.. _loaded.Keys];
}
