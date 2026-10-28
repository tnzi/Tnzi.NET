namespace Tnzi.AI.Skills.Middleware;

/// <summary>
/// 一组已激活技能折叠出的<b>有效约束</b>，以及「某个工具此刻能不能用」的判定。
/// 两个执行点共用同一份折叠与判定：<see cref="SkillConstraintMiddleware"/>（轮次开始，
/// 决定模型看得到哪些工具 + 模型 / Provider 覆盖）与 <see cref="SkillConstraintToolMiddleware"/>
/// （每次工具调用，决定放不放行）。判定只在这里写一次，两处才不会各自漂移。
/// </summary>
/// <remarks>
/// <para>合并规则（最严格优先，与 docs/modules/ai-skills.md「多 Skill 约束合并」一致）：</para>
/// <list type="bullet">
/// <item>工具限制<b>逐技能判定、取与</b>：受组管理的工具 T（组 G）要对<b>每一个</b>限制工具的技能都成立
/// 「T 在它的白名单里 或 G 在它的组里」。声明了组的技能放行组内全部工具，它自己的白名单在组之外<b>补充</b>放行单个工具；
/// 只声明白名单的技能只放行白名单里的；两者都没声明的技能不限制工具，也不清空别人的结果。</item>
/// <item>DeniedTools：并集；deny 永远胜出。</item>
/// <item>Model / Provider：按 Priority 降序逐个应用，最高优先级技能的值最后落定。</item>
/// </list>
/// <para>由此组取交集、白名单在声明了白名单的技能之间取交集，都是这条规则的特例。
/// ★ 此前把各技能的组与白名单先各自合并、再拿合并结果判定：只声明组 [file] 的技能 A 与只声明白名单 [web_search] 的
/// 技能 B 同时激活时，file 组的工具因「组在有效组内」放行、web_search 因「在白名单里」放行：多激活一条技能反而放宽了
/// B 自己的限制。不在注册表里的工具（MCP / 动态）组约束与白名单管不到，只有黑名单能按名字拦下。</para>
/// <para>
/// ★ 技能<b>只收窄、不扩权</b>：白名单里的工具名只用来让该工具躲过组过滤，
/// 从不按名字去全局注册表把 agent 没配置的工具拉进来（那会绕过 RequiredPermissions
/// 与审批包装，任何能建 User 技能的用户都能借此给 agent 加上管理员工具）。
/// </para>
/// </remarks>
public sealed class SkillConstraintSet
{
    /// <summary>技能管理工具：永不被技能约束拦下，否则一条技能就能把 agent 锁死在自己里面。</summary>
    private static readonly HashSet<string> SkillManagementTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "skill_search", "skill_get", "skill_activate", "skill_deactivate", "skill_get_resource"
    };

    private readonly IReadOnlyList<ToolRestriction> _restrictions;
    private readonly HashSet<string> _deniedTools;

    private SkillConstraintSet(
        IReadOnlyList<SkillDefinition> activeSkills,
        IReadOnlyList<ToolRestriction> restrictions,
        HashSet<string> deniedTools,
        string? model,
        string? provider)
    {
        ActiveSkills = activeSkills;
        _restrictions = restrictions;
        _deniedTools = deniedTools;
        EffectiveModel = model;
        EffectiveProvider = provider;
    }

    /// <summary>参与折叠的技能（Priority 降序）。</summary>
    public IReadOnlyList<SkillDefinition> ActiveSkills { get; }

    /// <summary>折叠后的模型（无覆盖时为传入的当前模型）。</summary>
    public string? EffectiveModel { get; }

    /// <summary>折叠后的 Provider（无覆盖时为传入的当前 Provider）。</summary>
    public string? EffectiveProvider { get; }

    /// <summary>是否有任何技能对工具施加了限制（组 / 白名单 / 黑名单）。</summary>
    public bool RestrictsTools => _restrictions.Count > 0 || _deniedTools.Count > 0;

    /// <summary>
    /// 折叠一组已激活技能。<paramref name="availableGroups"/> 是注册表里全部工具组；
    /// <paramref name="currentModel"/> / <paramref name="currentProvider"/> 是无覆盖时的基线。
    /// </summary>
    public static SkillConstraintSet Resolve(
        IReadOnlyList<SkillDefinition> activeSkills,
        IReadOnlyList<string> availableGroups,
        string? currentModel,
        string? currentProvider,
        ISkillConstraintEnforcer enforcer)
    {
        Check.NotNull(activeSkills);
        Check.NotNull(availableGroups);
        Check.NotNull(enforcer);

        var ordered = activeSkills.OrderByDescending(s => s.Priority).ToList();

        var ctx = new SkillConstraintContext
        {
            AvailableToolGroups = [.. availableGroups],
            CurrentModel = currentModel,
            CurrentProvider = currentProvider
        };

        var restrictions = new List<ToolRestriction>();
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in ordered)
        {
            // 顺序折叠只用来落定 Model / Provider（高优先级最后落定）。
            var result = enforcer.Apply(skill, ctx);
            ctx.AvailableToolGroups = result.EffectiveToolGroups;
            ctx.CurrentModel = result.EffectiveModel;
            ctx.CurrentProvider = result.EffectiveProvider;

            if (result.DeniedTools is { Count: > 0 })
                denied.UnionWith(result.DeniedTools);

            if (ToolRestriction.For(skill, result, availableGroups, enforcer) is { } restriction)
                restrictions.Add(restriction);
        }

        return new SkillConstraintSet(ordered, restrictions, denied, ctx.CurrentModel, ctx.CurrentProvider);
    }

    /// <summary>
    /// 判定一个工具此刻是否可用。<paramref name="group"/> 为 null 表示该工具不在注册表里
    /// （MCP / OpenAPI / 动态工具）：组约束与白名单管不到它，只有黑名单能按名字拦下。
    /// </summary>
    /// <returns>可用返回 null；否则返回给模型看的拒绝原因（英文）。</returns>
    public string? GetDenialReason(string toolName, string? group)
    {
        Check.NotNullOrWhiteSpace(toolName);

        if (SkillManagementTools.Contains(toolName))
            return null;

        if (_deniedTools.Contains(toolName))
            return $"Tool '{toolName}' is blocked by an active skill (tool-blacklist).";

        // 不在注册表里的工具：组约束与白名单都管不到，只有上面的黑名单能拦。
        if (group == null)
            return null;

        // 每一条限制都要放行它：白名单里的工具躲过该技能的组过滤（"补充白名单" 语义）。
        // 白名单只对 agent 已经拥有的工具有意义，从不据此把注册表里的工具拉进 agent。
        foreach (var restriction in _restrictions)
        {
            if (restriction.Allows(toolName, group))
                continue;

            return restriction.Groups != null
                ? $"Tool '{toolName}' (group '{group}') is outside the tool groups permitted by the active skills."
                : $"Tool '{toolName}' is not in the tool whitelist of the active skills.";
        }

        return null;
    }

    /// <summary>
    /// 一个技能自己的工具限制：它声明的组（经执行器对照全部可用组求出）与它自己的白名单；两者都没有的技能不产生限制。
    /// </summary>
    private sealed record ToolRestriction(HashSet<string>? Groups, HashSet<string>? Tools)
    {
        public static ToolRestriction? For(
            SkillDefinition skill, SkillConstraintResult sequentialResult, IReadOnlyList<string> availableGroups, ISkillConstraintEnforcer enforcer)
        {
            var tools = sequentialResult.EffectiveTools is { Count: > 0 }
                ? new HashSet<string>(sequentialResult.EffectiveTools, StringComparer.OrdinalIgnoreCase)
                : null;

            HashSet<string>? groups = null;
            if (skill.AllowedToolGroups is { Count: > 0 })
            {
                // 单独对照全部可用组求这个技能自己的组，而不是沿用顺序折叠里已被前面技能收窄过的那一份：
                // 后者会让前一个技能的组限制混进这一个技能的「白名单补充」判定里。
                var own = enforcer.Apply(skill, new SkillConstraintContext { AvailableToolGroups = [.. availableGroups] });
                groups = new HashSet<string>(own.EffectiveToolGroups, StringComparer.OrdinalIgnoreCase);
            }

            return groups == null && tools == null ? null : new ToolRestriction(groups, tools);
        }

        public bool Allows(string toolName, string group)
            => Tools?.Contains(toolName) == true || Groups?.Contains(group) == true;
    }
}
