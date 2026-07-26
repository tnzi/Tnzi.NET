namespace Tnzi.AI.Workflow.Engine;

/// <summary>
/// 工作流节点服务上下文默认实现 - 通过 IServiceProvider 解析依赖并提供作用域创建能力。
/// </summary>
internal class WorkflowNodeServiceContext : IWorkflowNodeServiceContext
{
    private readonly IServiceProvider _serviceProvider;

    public IAgentFactory AgentFactory { get; }
    public IRepository<Agent, Guid>? AgentRepository { get; }

    public WorkflowNodeServiceContext(
        IServiceProvider serviceProvider,
        IAgentFactory agentFactory,
        IRepository<Agent, Guid>? agentRepository = null)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        AgentFactory = Check.NotNull(agentFactory);
        AgentRepository = agentRepository;
    }

    public IServiceScope CreateScope() => _serviceProvider.CreateScope();
}
