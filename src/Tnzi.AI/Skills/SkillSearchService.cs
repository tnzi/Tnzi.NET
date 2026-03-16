namespace Tnzi.AI.Skills;

/// <summary>
/// 技能搜索服务实现 — Tier 1 关键词加权评分，Tier 2 语义降级（预留）。
/// </summary>
/// <remarks>
/// 评分权重：Name ×3.0 | Tags ×2.0 | WhenToUse ×1.5 | Description ×1.0
/// </remarks>
public class SkillSearchService : ISkillSearchService
{
    private readonly IEmbeddingService? _embeddingService;

    // 字段权重
    private const double NameWeight = 3.0;
    private const double TagWeight = 2.0;
    private const double WhenToUseWeight = 1.5;
    private const double DescriptionWeight = 1.0;

    public SkillSearchService(IEmbeddingService? embeddingService = null)
    {
        _embeddingService = embeddingService;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SkillDefinition>> SearchAsync(
        IReadOnlyList<SkillDefinition> candidates,
        string query,
        int maxResults,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || candidates.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<SkillDefinition>>([]);
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Tier 1: keyword search
        var scored = new List<(SkillDefinition Skill, double Score)>();
        foreach (var skill in candidates)
        {
            var score = ComputeScore(skill, tokens);
            if (score > 0)
            {
                scored.Add((skill, score));
            }
        }

        if (scored.Count > 0)
        {
            var results = scored
                .OrderByDescending(x => x.Score)
                .Take(maxResults)
                .Select(x => x.Skill)
                .ToList();
            return Task.FromResult<IReadOnlyList<SkillDefinition>>(results);
        }

        // Tier 2: semantic fallback (TODO: implement when IEmbeddingService integration is ready)
        // if (_embeddingService != null) { ... }

        return Task.FromResult<IReadOnlyList<SkillDefinition>>([]);
    }

    private static double ComputeScore(SkillDefinition skill, string[] tokens)
    {
        double total = 0;
        foreach (var token in tokens)
        {
            // Name match
            if (!string.IsNullOrEmpty(skill.Name) &&
                skill.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                total += NameWeight;
            }

            // Tags match — any tag contains the token
            if (skill.Tags.Any(tag => tag.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                total += TagWeight;
            }

            // WhenToUse match
            if (!string.IsNullOrEmpty(skill.WhenToUse) &&
                skill.WhenToUse.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                total += WhenToUseWeight;
            }

            // Description match
            if (!string.IsNullOrEmpty(skill.Description) &&
                skill.Description.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                total += DescriptionWeight;
            }
        }
        return total;
    }
}
