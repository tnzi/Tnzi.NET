namespace Tnzi.AI.Middleware;

/// <summary>
/// 技能约束中间件 — 在上下文注入后、用量日志前执行约束过滤
/// </summary>
public class SkillConstraintMiddleware : IAiMiddleware
{
    private readonly ILogger<SkillConstraintMiddleware> _logger;

    public int Order => 450;

    public SkillConstraintMiddleware(ILogger<SkillConstraintMiddleware> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        ApplyConstraints(context);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context,
        AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ApplyConstraints(context);
        await foreach (var chunk in next(context, cancellationToken))
            yield return chunk;
    }

    private void ApplyConstraints(AiMiddlewareContext context)
    {
        if (!context.Properties.TryGetValue("ActiveSkills", out var obj)
            || obj is not List<SkillDefinition> activeSkills
            || activeSkills.Count == 0)
            return;

        var enforcer = context.ServiceProvider.GetRequiredService<ISkillConstraintEnforcer>();
        var toolRegistry = context.ServiceProvider.GetRequiredService<IToolRegistry>();

        // Build tool-name→group mapping from registry
        var allDefs = toolRegistry.GetAllTools();
        var toolGroupMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in allDefs)
        {
            if (!string.IsNullOrEmpty(def.GroupName))
                toolGroupMap[def.Name] = def.GroupName;
        }

        var availableGroups = toolGroupMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var constraintCtx = new SkillConstraintContext
        {
            AvailableToolGroups = availableGroups,
            CurrentModel = context.EffectiveModel ?? context.Request.Model,
            CurrentProvider = context.EffectiveProvider ?? context.Request.Provider
        };

        // Apply each skill's constraints (highest priority first)
        foreach (var skill in activeSkills.OrderByDescending(s => s.Priority))
        {
            var result = enforcer.Apply(skill, constraintCtx);
            constraintCtx.AvailableToolGroups = result.EffectiveToolGroups;
            constraintCtx.CurrentModel = result.EffectiveModel;
            constraintCtx.CurrentProvider = result.EffectiveProvider;
        }

        // Filter AdditionalTools: remove tools whose group is not in effective groups
        if (constraintCtx.AvailableToolGroups.Count < availableGroups.Count)
        {
            var allowed = new HashSet<string>(constraintCtx.AvailableToolGroups, StringComparer.OrdinalIgnoreCase);
            var removed = context.AdditionalTools.RemoveAll(t =>
                t.Name != null
                && toolGroupMap.TryGetValue(t.Name, out var group)
                && !allowed.Contains(group));

            if (removed > 0)
                _logger.LogInformation("Skill constraints filtered {Count} tools", removed);
        }

        // Set effective model/provider overrides
        context.EffectiveModel = constraintCtx.CurrentModel;
        context.EffectiveProvider = constraintCtx.CurrentProvider;

        _logger.LogDebug("Applied constraints from {Count} active skills", activeSkills.Count);
    }
}
