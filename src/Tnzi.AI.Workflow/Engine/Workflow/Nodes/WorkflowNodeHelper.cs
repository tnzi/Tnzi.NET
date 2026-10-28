namespace Tnzi.AI.Workflow.Engine.Nodes;

/// <summary>
/// 工作流节点共享辅助方法 - 封装 Agent 解析与创建的重复模式
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
    /// <remarks>
    /// <c>fallbackInstructions</c> 是步骤与 Agent 都没给系统提示词时才用的节点缺省提示词。它排在 Agent 之后：
    /// 选了 Agent 的节点用的是那个 Agent 自己的系统提示词，节点要求的输出格式（verdict JSON、路由键）由节点放在用户消息里。
    /// </remarks>
    internal static async Task<IAgentExecutor> CreateAgentExecutorAsync(
        Guid? agentId,
        IWorkflowNodeServiceContext serviceContext,
        string? provider,
        string? model,
        string? instructions,
        string name,
        CancellationToken cancellationToken = default,
        string? fallbackInstructions = null)
    {
        var config = new RefHolder
        {
            Provider = provider,
            Model = model,
            Instructions = instructions
        };

        await ResolveAgentConfigAsync(agentId, serviceContext, config, cancellationToken);
        if (string.IsNullOrWhiteSpace(config.Instructions) && fallbackInstructions != null)
            config.Instructions = fallbackInstructions;

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
    /// 节点执行用的 Agent：节点专属配置键（如 <c>reviewerAgentId</c>）优先，缺失时用步骤的 <see cref="WorkflowStepDto.AgentId"/>。
    /// </summary>
    /// <remarks>
    /// Builder 的 <c>AddReviewStep(stepId, agentId)</c> 一类方法、内置模板与可视化编辑器的 Agent 选择器都把
    /// Agent 写在 <see cref="WorkflowStepDto.AgentId"/> 上；只读配置键会让这些入口选的 Agent 一律不生效。
    /// </remarks>
    internal static Guid? ResolveNodeAgentId(WorkflowStepDto step, Dictionary<string, string> config, string configKey)
        => ParseAgentIdFromConfig(config, configKey) ?? step.AgentId;

    /// <summary>
    /// 步骤作者写的 <see cref="WorkflowStepDto.Instructions"/> 是模板：<c>{{stepId}}</c> / <c>{{input}}</c> 在节点执行时替换。
    /// </summary>
    /// <remarks>
    /// 只有作者写的字符串（Instructions、条件、transform 模板）是模板；初始输入与上游输出是数据，不做替换 ——
    /// 否则一段恰好含 <c>{{input}}</c> 的上游产出（生成的 Jinja / Handlebars 模板）会被悄悄换成别的内容。
    /// </remarks>
    internal static string? ResolveInstructions(WorkflowStepDto step, WorkflowState state)
        => string.IsNullOrWhiteSpace(step.Instructions) ? step.Instructions : state.ResolveTemplate(step.Instructions);

    /// <summary>
    /// 收集上游输入 - 无依赖返回初始输入，单依赖直接返回文本，多依赖以 [depId] 分段拼接
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
    /// 按步骤的 <see cref="WorkflowStepDto.DependsOn"/> 从工作流状态收集输入文本：
    /// 无依赖返回初始输入；单依赖返回该依赖输出（缺失时回退初始输入）；
    /// 多依赖以 <c>[depId]</c> 分段拼接（跳过空输出，全空时回退初始输入）。
    /// <para>
    /// 引擎层（node input summary）与 AgentNode 共用此唯一实现，避免三处重复。
    /// </para>
    /// </summary>
    internal static string BuildStepInput(WorkflowStepDto step, WorkflowState state)
    {
        if (step.DependsOn == null || step.DependsOn.Count == 0)
            return state.InitialInput;

        if (step.DependsOn.Count == 1)
            return state.GetOutput(step.DependsOn[0])?.Text ?? state.InitialInput;

        var sb = new StringBuilder();
        foreach (var depId in step.DependsOn)
        {
            var output = state.GetOutput(depId);
            if (output == null || string.IsNullOrWhiteSpace(output.Text)) continue;

            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"[{depId}]");
            sb.AppendLine(output.Text);
        }

        return sb.Length > 0 ? sb.ToString().TrimEnd() : state.InitialInput;
    }

    /// <summary>
    /// 收集所有上游输出 - 无依赖返回初始输入，有依赖时始终以 [depId] 分段拼接（不做单依赖短路）
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
