namespace Tnzi.AI.Skills.Middleware;

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

        var toolGroupMap = _toolGroupMap.Value;
        var availableGroups = toolGroupMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var constraintCtx = new SkillConstraintContext
        {
            AvailableToolGroups = availableGroups,
            CurrentModel = context.EffectiveModel ?? context.Request.Model,
            CurrentProvider = context.EffectiveProvider ?? context.Request.Provider
        };

        // Accumulate individual tool allow/deny across all active skills.
        //
        // AllowedTools semantics = INTERSECTION (most-restrictive-wins), matching the
        // tool-group filtering done by SkillConstraintEnforcer.Apply and the documented
        // "strictest wins" contract. Rationale: a per-skill AllowedTools whitelist means
        // "while this skill is active, only these tools are permitted"; with multiple
        // active skills the agent must satisfy every whitelist, so only tools that ALL
        // whitelisting skills permit survive.
        //
        // A skill with NO AllowedTools imposes no individual-tool restriction and must
        // NOT collapse the intersection to empty — so only skills that actually declare
        // a non-empty AllowedTools participate in the intersection (`accumulatedAllowedTools`
        // stays null until the first whitelisting skill is seen).
        //
        // DeniedTools remains a UNION (any skill's deny blocks the tool) — deny always wins.
        HashSet<string>? accumulatedAllowedTools = null;
        var accumulatedDeniedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in activeSkills.OrderByDescending(s => s.Priority))
        {
            var result = _enforcer.Apply(skill, constraintCtx);
            constraintCtx.AvailableToolGroups = result.EffectiveToolGroups;
            constraintCtx.CurrentModel = result.EffectiveModel;
            constraintCtx.CurrentProvider = result.EffectiveProvider;

            if (result.EffectiveTools is { Count: > 0 })
            {
                var skillTools = new HashSet<string>(result.EffectiveTools, StringComparer.OrdinalIgnoreCase);
                if (accumulatedAllowedTools == null)
                    accumulatedAllowedTools = skillTools; // first whitelisting skill seeds the set
                else
                    accumulatedAllowedTools.IntersectWith(skillTools); // narrow to common tools
            }

            if (result.DeniedTools is { Count: > 0 })
            {
                foreach (var tool in result.DeniedTools)
                    accumulatedDeniedTools.Add(tool);
            }
        }

        // A non-null set means at least one active skill declared an AllowedTools whitelist.
        // An empty (but non-null) set means multiple skills declared *disjoint* whitelists,
        // so no grouped tool satisfies every whitelist → all grouped tools must be removed.
        var hasAllowedToolsWhitelist = accumulatedAllowedTools != null;

        // Filter AdditionalTools: remove tools whose group is not in effective groups
        if (constraintCtx.AvailableToolGroups.Count < availableGroups.Count)
        {
            var allowed = new HashSet<string>(constraintCtx.AvailableToolGroups, StringComparer.OrdinalIgnoreCase);
            var removed = context.AdditionalTools.RemoveAll(t =>
                t.Name != null
                && toolGroupMap.TryGetValue(t.Name, out var group)
                && !allowed.Contains(group)
                && accumulatedAllowedTools?.Contains(t.Name) != true); // individual whitelist overrides group removal

            if (removed > 0)
                _logger.LogInformation("Skill constraints filtered {Count} tools by group", removed);
        }

        // Apply individual tool allow list (keep only allowed + ungrouped tools).
        // Runs whenever a whitelist exists (even if empty → removes every grouped tool).
        if (hasAllowedToolsWhitelist)
        {
            var whitelist = accumulatedAllowedTools!;
            var allowedCount = context.AdditionalTools.RemoveAll(t =>
                t.Name != null
                && !whitelist.Contains(t.Name)
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
        if (accumulatedAllowedTools is { Count: > 0 })
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
