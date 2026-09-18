namespace Tnzi.AI.Skills.Middleware;

/// <summary>
/// 技能约束中间件（Order 450）- 轮次开始时按已激活技能收窄模型可见的工具列表，并应用模型 / Provider 覆盖。
/// </summary>
/// <remarks>
/// <para>
/// 激活集来自 <see cref="ISkillActivationTracker"/>（Scoped；轮次开始由本中间件从线程元数据恢复，
/// <c>SkillContextProvider</c> 跑到时也会恢复，但那一步可被预算 / 配置跳过，故不能只靠它）。
/// 此前它读 <c>Properties["ActiveSkills"]</c>，而那把键在真实请求上从未被写入
/// （唯一写者在 <c>skill_activate</c> 被调用之前就已经跑完），于是本中间件从未执行过任何约束。
/// </para>
/// <para>
/// 本中间件只负责「不给模型看」：从 <see cref="AiMiddlewareContext.AdditionalTools"/> 里移除被约束的工具，
/// 并把 agent 自带工具里被约束的名字写进 <see cref="AiMiddlewareContext.ExcludedToolNames"/>
/// 交 AgentRuntime 摘掉。真正的拦截在工具执行管线的 <see cref="SkillConstraintToolMiddleware"/>，
/// 那一层在激活当轮就生效，也不依赖这里。
/// </para>
/// <para>
/// ★ 不再按白名单从全局注册表<b>注入</b>工具：技能只收窄、不扩权（详见 <see cref="SkillConstraintSet"/>）。
/// </para>
/// </remarks>
public class SkillConstraintMiddleware : IAiMiddleware
{
    private readonly ISkillConstraintEnforcer _enforcer;
    private readonly IToolRegistry _toolRegistry;
    private readonly ISkillActivationTracker _activationTracker;
    private readonly ILogger<SkillConstraintMiddleware> _logger;
    private readonly Lazy<Dictionary<string, string>> _toolGroupMap;

    public int Order => AiMiddlewareOrders.SkillConstraint;

    public SkillConstraintMiddleware(
        ISkillConstraintEnforcer enforcer,
        IToolRegistry toolRegistry,
        ISkillActivationTracker activationTracker,
        ILogger<SkillConstraintMiddleware> logger)
    {
        _enforcer = Check.NotNull(enforcer);
        _toolRegistry = Check.NotNull(toolRegistry);
        _activationTracker = Check.NotNull(activationTracker);
        _logger = Check.NotNull(logger);
        _toolGroupMap = new Lazy<Dictionary<string, string>>(() => BuildToolGroupMap(_toolRegistry));
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        await RestoreActivationSetAsync(context, cancellationToken);
        ApplyConstraints(context);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context,
        AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await RestoreActivationSetAsync(context, cancellationToken);
        ApplyConstraints(context);
        await foreach (var chunk in next(context, cancellationToken))
            yield return chunk;
    }

    /// <summary>
    /// 把上一轮结束时的激活集从线程恢复到本作用域的跟踪器里 —— 无条件，不依赖内容注入那一步。
    /// </summary>
    /// <remarks>
    /// <c>SkillContextProvider</c> 也会恢复，但它只在 <c>CompositeContextProvider</c> 真的调到它时才跑：
    /// Memory / RAG 先把 token 预算用完、agent 配置 <c>disableContextProviders</c>、provider 被 <c>IsEnabled</c>
    /// 滤掉，任一情况下那一步都不会执行，而工具执行管线读的是同一个跟踪器 —— 于是这一轮整个无约束运行，
    /// 唯一痕迹是一条 Debug 日志。本中间件在每轮开始、agent 循环之前必跑，把恢复钉在这里。
    /// <see cref="ISkillActivationTracker.RestoreAsync"/> 每作用域幂等，重复调用无代价；
    /// 读失败它会抛，本轮随之失败而不是放行（失败方向关闭）。
    /// </remarks>
    private Task RestoreActivationSetAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
        => context.Request.ThreadId is { } threadId
            ? _activationTracker.RestoreAsync(threadId, cancellationToken)
            : Task.CompletedTask;

    /// <summary>注册表里「工具名 → 工具组」的映射（不区分大小写）。两个约束中间件共用。</summary>
    internal static Dictionary<string, string> BuildToolGroupMap(IToolRegistry toolRegistry)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in toolRegistry.GetAllTools())
        {
            if (!string.IsNullOrEmpty(def.GroupName))
                map[def.Name] = def.GroupName;
        }
        return map;
    }

    private void ApplyConstraints(AiMiddlewareContext context)
    {
        var activeSkills = _activationTracker.ActivatedSkills;
        if (activeSkills.Count == 0)
            return;

        var toolGroupMap = _toolGroupMap.Value;
        var availableGroups = toolGroupMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var constraints = SkillConstraintSet.Resolve(
            activeSkills,
            availableGroups,
            context.EffectiveModel ?? context.Request.Model,
            context.EffectiveProvider ?? context.Request.Provider,
            _enforcer);

        if (constraints.RestrictsTools)
        {
            // 中间件注入的工具：直接从列表里移除。
            var removed = context.AdditionalTools.RemoveAll(t =>
                t.Name != null && constraints.GetDenialReason(t.Name, toolGroupMap.GetValueOrDefault(t.Name)) != null);

            // Agent 自带的工具：记名字，交 AgentRuntime 在合并后摘掉。
            var excluded = 0;
            foreach (var tool in context.Agent.Agent?.Tools ?? [])
            {
                if (tool.Name != null
                    && constraints.GetDenialReason(tool.Name, toolGroupMap.GetValueOrDefault(tool.Name)) != null
                    && context.ExcludedToolNames.Add(tool.Name))
                    excluded++;
            }

            if (removed > 0 || excluded > 0)
                _logger.LogInformation("Skill constraints withheld {Removed} injected and {Excluded} agent tools from the model", removed, excluded);
        }

        context.EffectiveModel = constraints.EffectiveModel;
        context.EffectiveProvider = constraints.EffectiveProvider;

        _logger.LogDebug("Applied constraints from {Count} active skills", activeSkills.Count);
    }
}
