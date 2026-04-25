namespace Tnzi.AI.Services;

public class NoOpSkillSearchService : ISkillSearchService, INoOpService
{
    public Task<IReadOnlyList<SkillDefinition>> SearchAsync(
        IReadOnlyList<SkillDefinition> candidates,
        string query,
        int maxResults,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SkillDefinition>>(Array.Empty<SkillDefinition>());
}
