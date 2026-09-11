namespace Tnzi.AI.Engine;

/// <summary>
/// 子 Agent 执行上下文标记 —— 所有起子 Agent 的路径共用这一处写入点。
/// </summary>
/// <remarks>
/// <para>
/// 「是不是子 Agent」的含义：<b>当前在跑的 Agent 不是这次请求最初面向的那一个</b>。
/// 三种形态都算：父 Agent 把子 Agent 当工具调用（AgentAsTools）、后台起的子运行（<c>spawn_agent</c>）、
/// 以及由模型自己选出目标后转接过去（Handoff / Router）。三者的共同风险面是一样的：
/// 接下来执行工具的那个 Agent 身份是模型选的，不是用户指定的。
/// </para>
/// <para>
/// <c>ToolPermissionRule.IsSubAgentOnly</c> 只在这个标记为 true 时匹配。少写一处，
/// 针对子 Agent 收紧工具的规则在那条路径上就一条也不生效，而管理端仍把它显示为一条已启用的规则。
/// </para>
/// </remarks>
internal static class SubAgentContext
{
    /// <summary>
    /// 在当前执行上下文里标记「接下来跑的是子 Agent」，并先把父级的会话规则存档。
    /// </summary>
    /// <param name="accessor">执行上下文访问器；为 null 时无操作</param>
    /// <param name="serviceProvider">用于解析可选的 <see cref="IToolPermissionEvaluator"/></param>
    /// <param name="subAgentName">子 Agent 名称（类型名或目标 Agent 名）</param>
    internal static void Mark(
        IAgentExecutionContextAccessor? accessor,
        IServiceProvider? serviceProvider,
        string? subAgentName)
    {
        if (accessor == null)
        {
            return;
        }

        // 覆盖上下文之前先存档父级的会话级规则：子 Agent 结束后要按它们还原，
        // 丢掉一条 Deny 类会话规则就是 fail-open。
        var permissionEvaluator = serviceProvider?.GetService<IToolPermissionEvaluator>();
        if (permissionEvaluator != null)
        {
            var sessionRules = permissionEvaluator.GetSessionRules();
            if (sessionRules.Count > 0)
            {
                accessor.Properties[ContextPropertyKeys.ParentSessionRules] = sessionRules;
            }
        }

        accessor.Properties[ContextPropertyKeys.IsSubAgent] = true;
        if (!string.IsNullOrWhiteSpace(subAgentName))
        {
            accessor.Properties[ContextPropertyKeys.SubAgentName] = subAgentName;
        }
    }
}
