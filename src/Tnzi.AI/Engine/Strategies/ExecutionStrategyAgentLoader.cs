namespace Tnzi.AI.Engine.Strategies;

public static class ExecutionStrategyAgentLoader
{
    public static async Task<IAgentExecutor?> ResolveAgentAsync(Guid agentId, ExecutionStrategyContext context, CancellationToken ct)
    {
        // 子 / 目标 Agent 与主路径同一条可见性：租户调用者也解析得到宿主级共享定义。
        var scope = SharedAgentScope.Resolve(context.ServiceProvider);
        var entity = scope.IsTenantCaller
            ? await scope.Apply(context.AgentRepository.AsQueryable()).FirstOrDefaultAsync(a => a.Id == agentId, ct)
            : await context.AgentRepository.GetAsync(agentId, ct);
        if (entity == null || !entity.IsEnabled) return null;

        // Tool grants are owned by the junction (the entity no longer carries JSON resource columns).
        var grantService = context.ServiceProvider.GetRequiredService<IAgentGrantService>();
        var grants = await grantService.GetGrantsAsync(entity.Id, ct);
        var toolGroups = grants.ToolGroups.Count > 0 ? grants.ToolGroups.ToList() : null;
        // per-tool grants (GrantType=Tool): expand to single tool names and flow alongside the
        // tool groups into the factory - without this a child/target agent in a multi-agent flow
        // silently loses its individually granted tools. null-when-empty mirrors the primary path.
        var toolNames = grants.ToolNames.Count > 0 ? grants.ToolNames.ToList() : null;

        // NOTE: SkillSlugs / KnowledgeBaseIds are intentionally NOT wired here. They are consumed ONLY by
        // ContextInjectionMiddleware (→ SkillContextProvider / TextSearchProvider), which runs in the
        // IAiMiddleware pipeline driven by IAgentRuntime. Child/target agents in Handoff/Router/AgentAsTools
        // strategies are executed via a bare AgentExecutor.ExecuteAsync (see *ExecutionStrategy.cs) and do NOT
        // pass through that pipeline, so skill-whitelist / KB-scoping has no consumer on this path.
        // IAgentFactory.CreateAgentAsync also has no skillSlugs/knowledgeBaseIds parameters (the primary path
        // delivers them through AgentResolution, not the factory). Honoring skill/KB scoping for child agents
        // would require routing them through the middleware pipeline - a larger change, out of scope here.
        // Today child agents in strategy execution honor tool grants only (groups + per-tool names).

        // 子 agent 与主路径过同一道 RequiredPermissions 门。grant 是 agent 作者授予 agent 的，不是调用者持有的权限；
        // 不传 userPermissions（null）等于放弃门控 —— 一个没有 ai.tools.sandbox 的用户对着 Router 父 agent 说话，
        // 被授予 sandbox 组的子 agent 就带着宿主 shell 替他跑。
        var permissionResolver = context.ServiceProvider.GetRequiredService<IUserToolPermissionResolver>();
        var userPermissions = await permissionResolver.ResolveAsync(toolGroups, toolNames, ct);
        return await context.AgentFactory.CreateAgentAsync(
            entity.Provider, entity.Model, entity.Instructions, entity.Name,
            toolGroups, entity.Temperature, entity.MaxTokens,
            userPermissions: userPermissions, toolNames: toolNames, agentId: entity.Id, ct: ct);
    }

    public static string GetLatestUserQuestion(List<ChatMessage> messages)
    {
        return messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "User needs assistance.";
    }

    public static TokenUsageDto? BuildUsage(int promptTokens, int completionTokens)
    {
        return promptTokens > 0 || completionTokens > 0
            ? new TokenUsageDto
            {
                InputTokens = promptTokens,
                OutputTokens = completionTokens,
                TotalTokens = promptTokens + completionTokens
            }
            : null;
    }
}
