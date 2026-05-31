namespace Tnzi.AI.Middleware;

/// <summary>
/// 技能约束中间件 — 在上下文注入后、用量日志前执行约束过滤
/// </summary>
public class SkillConstraintMiddleware : IAiMiddleware
{
    private readonly ISkillConstraintEnforcer _enforcer;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<SkillConstraintMiddleware> _logger;
    private readonly Lazy<Dictionary<string, string>> _toolGroupMap;

    public int Order => AiMiddlewareOrders.SkillConstraint;

    public SkillConstraintMiddleware(ISkillConstraintEnforcer enforcer, IToolRegistry toolRegistry, ILogger<SkillConstraintMiddleware> logger)
    {
        _enforcer = Check.NotNull(enforcer);
        _toolRegistry = Check.NotNull(toolRegistry);
        _logger = Check.NotNull(logger);
        _toolGroupMap = new Lazy<Dictionary<string, string>>(() =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in _toolRegistry.GetAllTools())
            {
                if (!string.IsNullOrEmpty(def.GroupName))
                    map[def.Name] = def.GroupName;
            }
            return map;
        });
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware)
            return await next(context, cancellationToken);

        ApplyConstraints(context);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context,
        AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

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

        var toolGroupMap = _toolGroupMap.Value;
        var availableGroups = toolGroupMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var constraintCtx = new SkillConstraintContext
        {
            AvailableToolGroups = availableGroups,
            CurrentModel = context.EffectiveModel ?? context.Request.Model,
            CurrentProvider = context.EffectiveProvider ?? context.Request.Provider
        };

        // Accumulate individual tool allow/deny across all active skills
        var accumulatedAllowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accumulatedDeniedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasAllowedTools = false;

        foreach (var skill in activeSkills.OrderByDescending(s => s.Priority))
        {
            var result = _enforcer.Apply(skill, constraintCtx);
            constraintCtx.AvailableToolGroups = result.EffectiveToolGroups;
            constraintCtx.CurrentModel = result.EffectiveModel;
            constraintCtx.CurrentProvider = result.EffectiveProvider;

            if (result.EffectiveTools is { Count: > 0 })
            {
                hasAllowedTools = true;
                foreach (var tool in result.EffectiveTools)
                    accumulatedAllowedTools.Add(tool);
            }

            if (result.DeniedTools is { Count: > 0 })
            {
                foreach (var tool in result.DeniedTools)
                    accumulatedDeniedTools.Add(tool);
            }
        }

        // Filter AdditionalTools: remove tools whose group is not in effective groups
        if (constraintCtx.AvailableToolGroups.Count < availableGroups.Count)
        {
            var allowed = new HashSet<string>(constraintCtx.AvailableToolGroups, StringComparer.OrdinalIgnoreCase);
            var removed = context.AdditionalTools.RemoveAll(t =>
                t.Name != null
                && toolGroupMap.TryGetValue(t.Name, out var group)
                && !allowed.Contains(group)
                && !accumulatedAllowedTools.Contains(t.Name)); // individual whitelist overrides group removal

            if (removed > 0)
                _logger.LogInformation("Skill constraints filtered {Count} tools by group", removed);
        }

        // Apply individual tool allow list (keep only allowed + ungrouped tools)
        if (hasAllowedTools)
        {
            var allowedCount = context.AdditionalTools.RemoveAll(t =>
                t.Name != null
                && !accumulatedAllowedTools.Contains(t.Name)
                && toolGroupMap.ContainsKey(t.Name)); // only filter grouped tools, keep ungrouped (MCP/dynamic)

            if (allowedCount > 0)
                _logger.LogInformation("Skill constraints allowed-only filter removed {Count} tools", allowedCount);
        }

        // Apply individual tool deny list
        if (accumulatedDeniedTools.Count > 0)
        {
            var deniedCount = context.AdditionalTools.RemoveAll(t =>
                t.Name != null && accumulatedDeniedTools.Contains(t.Name));

            if (deniedCount > 0)
                _logger.LogInformation("Skill constraints denied {Count} individual tools", deniedCount);
        }

        // Inject per-skill AllowedTools: ensure individually whitelisted tools are present in AdditionalTools.
        // Deny wins: skip tools that are in the deny set even if they were also allowed by another skill.
        if (accumulatedAllowedTools.Count > 0)
        {
            var existingToolNames = new HashSet<string>(
                context.AdditionalTools.Where(t => t.Name != null).Select(t => t.Name!),
                StringComparer.OrdinalIgnoreCase);

            var allToolDefs = _toolRegistry.GetAllTools();
            var injectedCount = 0;

            foreach (var toolName in accumulatedAllowedTools)
            {
                // Deny always wins over allow (intersection semantics)
                if (accumulatedDeniedTools.Contains(toolName))
                    continue;

                if (existingToolNames.Contains(toolName))
                    continue;

                var toolDef = allToolDefs.FirstOrDefault(d =>
                    string.Equals(d.Name, toolName, StringComparison.OrdinalIgnoreCase));

                if (toolDef == null)
                    continue;

                // Create AIFunction from the tool definition's method info and provider type
                var toolInstance = toolDef.ProviderType != null
                    ? CreateToolFromDefinition(toolDef, context.ServiceProvider)
                    : null;

                if (toolInstance != null)
                {
                    context.AdditionalTools.Add(toolInstance);
                    injectedCount++;
                }
            }

            if (injectedCount > 0)
                _logger.LogInformation("Injected {Count} per-skill allowed tools into context", injectedCount);
        }

        context.EffectiveModel = constraintCtx.CurrentModel;
        context.EffectiveProvider = constraintCtx.CurrentProvider;

        _logger.LogDebug("Applied constraints from {Count} active skills", activeSkills.Count);
    }

    /// <summary>
    /// Creates an AITool from a ToolDefinition by resolving the provider instance from DI and wrapping the method.
    /// </summary>
    private static AITool? CreateToolFromDefinition(ToolDefinition toolDef, IServiceProvider serviceProvider)
    {
        try
        {
            var providerInstance = serviceProvider.GetService(toolDef.ProviderType);
            if (providerInstance == null)
                return null;

            return AIFunctionFactory.Create(toolDef.MethodInfo, providerInstance);
        }
        catch
        {
            return null;
        }
    }
}
