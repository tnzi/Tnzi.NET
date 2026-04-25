namespace Tnzi.AI.Services;

public class NoOpSkillStore : ISkillStore, INoOpService
{
    public Task<List<SkillDefinition>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult(new List<SkillDefinition>());

    public Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => Task.FromResult<SkillDefinition?>(null);
}
