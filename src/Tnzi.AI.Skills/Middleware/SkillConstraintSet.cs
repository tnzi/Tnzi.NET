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
/// <item>AllowedToolGroups：交集（经 <see cref="ISkillConstraintEnforcer"/> 逐技能收窄可用组）。</item>
/// <item>AllowedTools：<b>仅在声明了白名单的技能之间</b>取交集；未声明者不清空交集。</item>
/// <item>DeniedTools：并集；deny 永远胜出。</item>
/// <item>Model / Provider：按 Priority 降序逐个应用，最高优先级技能的值最后落定。</item>
/// </list>
/// <para>受组管理的工具 T（组 G）可用 ⇔ 未被拉黑 且（T 在白名单 或 G 在有效组内）；
/// 声明了组的技能放行组内全部工具，白名单在组之外<b>补充</b>放行单个工具；
/// 只声明白名单不声明组时，受组管理的工具只剩白名单里的。不在注册表里的工具（MCP / 动态）
/// 组约束与白名单管不到，只有黑名单能按名字拦下。</para>
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

    private readonly HashSet<string> _effectiveGroups;
    private readonly HashSet<string>? _allowedTools;
    private readonly HashSet<string> _deniedTools;
    private readonly bool _groupsDeclared;

    private SkillConstraintSet(
        IReadOnlyList<SkillDefinition> activeSkills,
        HashSet<string> effectiveGroups,
        bool groupsDeclared,
        HashSet<string>? allowedTools,
        HashSet<string> deniedTools,
        string? model,
        string? provider)
    {
        ActiveSkills = activeSkills;
        _effectiveGroups = effectiveGroups;
        _groupsDeclared = groupsDeclared;
        _allowedTools = allowedTools;
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
    public bool RestrictsTools => _groupsDeclared || _allowedTools != null || _deniedTools.Count > 0;

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

        HashSet<string>? allowed = null;
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in ordered)
        {
            var result = enforcer.Apply(skill, ctx);
            ctx.AvailableToolGroups = result.EffectiveToolGroups;
            ctx.CurrentModel = result.EffectiveModel;
            ctx.CurrentProvider = result.EffectiveProvider;

            if (result.EffectiveTools is { Count: > 0 })
            {
                var skillTools = new HashSet<string>(result.EffectiveTools, StringComparer.OrdinalIgnoreCase);
                if (allowed == null)
                    allowed = skillTools;
                else
                    allowed.IntersectWith(skillTools);
            }

            if (result.DeniedTools is { Count: > 0 })
                denied.UnionWith(result.DeniedTools);
        }

        var effectiveGroups = new HashSet<string>(ctx.AvailableToolGroups, StringComparer.OrdinalIgnoreCase);
        var groupsDeclared = ordered.Any(s => s.AllowedToolGroups is { Count: > 0 });

        return new SkillConstraintSet(ordered, effectiveGroups, groupsDeclared, allowed, denied, ctx.CurrentModel, ctx.CurrentProvider);
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

        // 白名单里的工具躲过组过滤（"补充白名单" 语义）；只对 agent 已经拥有的工具有意义，
        // 从不据此把注册表里的工具拉进 agent。
        if (_allowedTools?.Contains(toolName) == true)
            return null;

        if (_groupsDeclared)
        {
            return _effectiveGroups.Contains(group)
                ? null
                : $"Tool '{toolName}' (group '{group}') is outside the tool groups permitted by the active skills.";
        }

        if (_allowedTools != null)
            return $"Tool '{toolName}' is not in the tool whitelist of the active skills.";

        return null;
    }
}
