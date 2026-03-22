namespace Tnzi.AI.Skills;

/// <summary>
/// Tracks which skills have been loaded (via skill_get) within the current request scope.
/// Shared between SkillContextProvider and RequiresSkillToolMiddleware to ensure
/// a skill loaded in one component is recognized by the other.
/// </summary>
public interface ISkillLoadTracker
{
    /// <summary>
    /// Returns true if the skill with the given slug has been loaded in this scope.
    /// </summary>
    bool IsLoaded(string slug);

    /// <summary>
    /// Marks the skill with the given slug as loaded in this scope.
    /// </summary>
    void MarkLoaded(string slug);

    /// <summary>
    /// Gets all slugs that have been loaded in this scope.
    /// </summary>
    IReadOnlyCollection<string> LoadedSlugs { get; }
}
