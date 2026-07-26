
namespace Tnzi.AI.Engine.Strategies;

/// <summary>
/// 执行策略解析器 - 根据 AgentExecutionMode 枚举和配置 JSON 决定使用哪种执行策略
/// </summary>
public static class ExecutionStrategyResolver
{
    /// <summary>
    /// 根据执行模式枚举和配置 JSON 解析执行策略
    /// </summary>
    /// <param name="mode">Agent 执行模式</param>
    /// <param name="configuration">Agent.Configuration JSON 字符串（用于解析 handoff targets 等详细配置）</param>
    /// <returns>对应的执行策略实例</returns>
    public static IExecutionStrategy Resolve(AgentExecutionMode mode, string? configuration)
    {
        var config = AgentExecutionConfigDto.Deserialize(configuration);

        return mode switch
        {
            AgentExecutionMode.Single => SingleAgentStrategy.Instance,
            AgentExecutionMode.Handoff => ResolveHandoff(config?.Handoff),
            AgentExecutionMode.AgentAsTools => ResolveAgentAsTools(config?.AgentAsTools),
            AgentExecutionMode.Router => ResolveRouter(config?.Router),
            _ => throw new InvalidOperationException($"Unsupported AgentExecutionMode: {mode}")
        };
    }

    private static IExecutionStrategy ResolveHandoff(HandoffExecutionConfigDto? dto)
    {
        var config = new HandoffConfiguration();
        if (dto != null)
        {
            config.Targets = dto.Targets;
            if (dto.MaxHandoffs.HasValue) config.MaxHandoffs = dto.MaxHandoffs.Value;
            if (dto.AllowReturnToSource.HasValue) config.AllowReturnToSource = dto.AllowReturnToSource.Value;
        }

        return new HandoffExecutionStrategy(config);
    }

    private static IExecutionStrategy ResolveAgentAsTools(AgentAsToolsExecutionConfigDto? dto)
    {
        var config = new AgentAsTools.AgentAsToolsConfiguration();
        if (dto != null)
        {
            config.Agents = dto.Agents;
            if (dto.MaxConcurrentSubAgents.HasValue)
                config.MaxConcurrentSubAgents = dto.MaxConcurrentSubAgents.Value;
            if (dto.SubAgentTimeoutSeconds.HasValue)
                config.SubAgentTimeout = TimeSpan.FromSeconds(dto.SubAgentTimeoutSeconds.Value);
        }

        return new AgentAsTools.AgentAsToolsExecutionStrategy(config);
    }

    private static IExecutionStrategy ResolveRouter(RouterExecutionConfigDto? dto)
    {
        var config = new Router.RouterConfiguration();
        if (dto != null)
        {
            config.Targets = dto.Targets;
            if (dto.AllowDirectResponse.HasValue) config.AllowDirectResponse = dto.AllowDirectResponse.Value;
        }

        return new Router.RouterExecutionStrategy(config);
    }
}
