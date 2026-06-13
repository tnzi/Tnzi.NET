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
    /// <param name="serviceContext">节点服务上下文</param>
    /// <param name="config">步骤级配置持有者（仅在字段为空时回填）</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal static async Task ResolveAgentConfigAsync(
        Guid? agentId,
        IWorkflowNodeServiceContext serviceContext,
        RefHolder config,
        CancellationToken cancellationToken = default)
    {
        if (!agentId.HasValue)
            return;

        var agentRepo = serviceContext.AgentRepository;
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
    /// 解析 Agent 配置并创建 IAgentExecutor（封装完整的 resolve → create 流程）
    /// </summary>
    internal static async Task<IAgentExecutor> CreateAgentExecutorAsync(
        Guid? agentId,
        IWorkflowNodeServiceContext serviceContext,
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

        await ResolveAgentConfigAsync(agentId, serviceContext, config, cancellationToken);

        return await serviceContext.AgentFactory.CreateAgentAsync(
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
    /// 收集上游输入 — 无依赖返回初始输入，单依赖直接返回文本，多依赖以 [depId] 分段拼接
    /// </summary>
    internal static string CollectInput(WorkflowNodeContext context)
    {
        if (context.DependencyOutputs.Count == 0)
            return context.State.InitialInput;

        if (context.DependencyOutputs.Count == 1)
            return context.DependencyOutputs.Values.First().Text;

        return FormatDependencyOutputs(context.DependencyOutputs);
    }

    /// <summary>
    /// 收集所有上游输出 — 无依赖返回初始输入，有依赖时始终以 [depId] 分段拼接（不做单依赖短路）
    /// </summary>
    internal static string CollectAllUpstreamOutputs(WorkflowNodeContext context)
    {
        if (context.DependencyOutputs.Count == 0)
            return context.State.InitialInput;

        return FormatDependencyOutputs(context.DependencyOutputs);
    }

    /// <summary>
    /// 将依赖输出格式化为 [depId] 分段文本
    /// </summary>
    private static string FormatDependencyOutputs(IReadOnlyDictionary<string, WorkflowStepOutput> outputs)
    {
        var sb = new StringBuilder();
        foreach (var (depId, output) in outputs)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"[{depId}]");
            sb.AppendLine(output.Text);
        }

        return sb.ToString().TrimEnd();
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
