namespace Tnzi.AI.Engine.Workflow.Nodes;

/// <summary>
/// 工作流节点共享辅助方法 — 封装 Agent 解析与创建的重复模式
/// </summary>
internal static class WorkflowNodeHelper
{
    /// <summary>
    /// 从数据库加载 Agent 定义，将其 Provider/Model/Instructions 作为缺省值回填到步骤级配置
    /// </summary>
    /// <param name="agentId">Agent ID（为空则跳过）</param>
    /// <param name="scope">服务作用域</param>
    /// <param name="provider">步骤级 Provider（ref，仅在为空时回填）</param>
    /// <param name="model">步骤级 Model（ref，仅在为空时回填）</param>
    /// <param name="instructions">步骤级 Instructions（ref，仅在为空时回填）</param>
    /// <param name="agentName">Agent 名称输出（仅当从 DB 加载成功时赋值）</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal static async Task ResolveAgentConfigAsync(
        Guid? agentId,
        IServiceScope scope,
        RefHolder config,
        CancellationToken cancellationToken = default)
    {
        if (!agentId.HasValue)
            return;

        var agentRepo = scope.ServiceProvider.GetService<IRepository<Agent, Guid>>();
        if (agentRepo == null)
            return;

        var agent = await agentRepo.GetAsync(agentId.Value, cancellationToken);
        if (agent == null)
            return;

        config.Provider ??= agent.Provider;
        config.Model ??= agent.Model;
        config.Instructions ??= agent.Instructions;
        config.AgentName ??= agent.Name;
    }

    /// <summary>
    /// 解析 Agent 配置并创建 AgentExecutor（封装完整的 scope → resolve → create 流程）
    /// </summary>
    /// <param name="agentId">Agent ID（可选）</param>
    /// <param name="scope">服务作用域</param>
    /// <param name="provider">步骤级 Provider</param>
    /// <param name="model">步骤级 Model</param>
    /// <param name="instructions">系统指令（步骤级优先，Agent 定义作为缺省）</param>
    /// <param name="name">Agent 名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal static async Task<AgentExecutor> CreateAgentExecutorAsync(
        Guid? agentId,
        IServiceScope scope,
        string? provider,
        string? model,
        string? instructions,
        string name,
        CancellationToken cancellationToken = default)
    {
        var config = new RefHolder
        {
            Provider = provider,
            Model = model,
            Instructions = instructions
        };

        await ResolveAgentConfigAsync(agentId, scope, config, cancellationToken);

        var agentFactory = scope.ServiceProvider.GetRequiredService<IAgentFactory>();

        return await agentFactory.CreateAgentAsync(
            providerName: config.Provider,
            model: config.Model,
            instructions: config.Instructions,
            name: name,
            agentId: agentId,
            ct: cancellationToken);
    }

    /// <summary>
    /// 从 Configuration 字典中解析 Agent ID
    /// </summary>
    internal static Guid? ParseAgentIdFromConfig(Dictionary<string, string> config, string key)
    {
        if (config.TryGetValue(key, out var idStr) && Guid.TryParse(idStr, out var id))
            return id;

        return null;
    }

    /// <summary>
    /// 可变持有者，用于 ResolveAgentConfigAsync 回填缺省值
    /// </summary>
    internal sealed class RefHolder
    {
        public string? Provider { get; set; }
        public string? Model { get; set; }
        public string? Instructions { get; set; }
        public string? AgentName { get; set; }
    }
}
