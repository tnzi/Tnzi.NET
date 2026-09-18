namespace Tnzi.AI.Skills.Middleware;

/// <summary>
/// 工具执行中间件 - 每一次工具调用前按已激活技能的约束放行或拒绝。这是技能约束的<b>安全边界</b>。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="SkillConstraintMiddleware"/>（Order 450，轮次开始时收窄模型可见的工具列表）互补：
/// 那一层在 agent 循环开始前执行，模型在<b>本轮</b>里调用 <c>skill_activate</c> 之后它已经跑完；
/// 本中间件挂在工具执行管线上，激活之后的每一次调用都经过它，于是激活当轮即生效。
/// 它读的是同一个 <see cref="ISkillActivationTracker"/>，判定与 450 共用 <see cref="SkillConstraintSet"/>。
/// </para>
/// <para>
/// 拒绝时不调用 <c>next</c>：返回一段给模型看的说明，并填 <see cref="ToolExecutionContext.FailureReason"/>
/// 让执行器把该次调用记为失败。工具组按名字从注册表查（<see cref="ToolExecutionContext.ToolGroup"/>
/// 只对 agent 工具组解析出来的工具有值，注入工具拿不到）。
/// </para>
/// </remarks>
public class SkillConstraintToolMiddleware : IToolExecutionMiddleware
{
    private readonly ISkillConstraintEnforcer _enforcer;
    private readonly IToolRegistry _toolRegistry;
    private readonly ISkillActivationTracker _activationTracker;
    private readonly ILogger<SkillConstraintToolMiddleware> _logger;
    private readonly Lazy<Dictionary<string, string>> _toolGroupMap;

    public SkillConstraintToolMiddleware(
        ISkillConstraintEnforcer enforcer,
        IToolRegistry toolRegistry,
        ISkillActivationTracker activationTracker,
        ILogger<SkillConstraintToolMiddleware> logger)
    {
        _enforcer = Check.NotNull(enforcer);
        _toolRegistry = Check.NotNull(toolRegistry);
        _activationTracker = Check.NotNull(activationTracker);
        _logger = Check.NotNull(logger);
        _toolGroupMap = new Lazy<Dictionary<string, string>>(() => SkillConstraintMiddleware.BuildToolGroupMap(_toolRegistry));
    }

    public async Task<object?> InvokeAsync(ToolExecutionContext context, Func<Task<object?>> next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        var activeSkills = _activationTracker.ActivatedSkills;
        if (activeSkills.Count == 0 || string.IsNullOrWhiteSpace(context.ToolName))
            return await next();

        var toolGroupMap = _toolGroupMap.Value;
        var availableGroups = toolGroupMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var constraints = SkillConstraintSet.Resolve(activeSkills, availableGroups, currentModel: null, currentProvider: null, _enforcer);
        if (!constraints.RestrictsTools)
            return await next();

        var group = context.ToolGroup ?? toolGroupMap.GetValueOrDefault(context.ToolName);
        var reason = constraints.GetDenialReason(context.ToolName, group);
        if (reason == null)
            return await next();

        _logger.LogInformation("Skill constraints blocked tool '{ToolName}': {Reason}", context.ToolName, reason);
        context.FailureReason = reason;

        var activeSlugs = string.Join(", ", activeSkills.Select(s => s.Slug));
        return $"{reason} Active skills: [{activeSlugs}]. Use skill_deactivate if the skill is no longer needed, otherwise choose a permitted tool.";
    }
}
