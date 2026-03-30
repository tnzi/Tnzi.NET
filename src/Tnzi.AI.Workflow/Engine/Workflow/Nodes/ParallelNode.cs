namespace Tnzi.AI.Engine.Workflow.Nodes;

/// <summary>
/// 并行节点 — 将输入复制给 N 个子 Agent 并行执行，合并输出
/// </summary>
/// <remarks>
/// 配置项：
/// - workers: JSON 数组，格式 [{"stepId": "w1", "agentId": "guid"}, ...]
///   每个 worker 配置一个 stepId（用于标识输出）和 agentId（Agent 定义 ID）
/// </remarks>
public class ParallelNode : IWorkflowNode
{
    private readonly IWorkflowNodeServiceContext _nodeContext;
    private readonly ILogger<ParallelNode> _logger;

    public string NodeType => WorkflowNodeTypes.Parallel;

    public ParallelNode(IWorkflowNodeServiceContext nodeContext, ILogger<ParallelNode> logger)
    {
        _nodeContext = Check.NotNull(nodeContext);
        _logger = Check.NotNull(logger);
    }

    public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var state = context.State;
        var config = step.Configuration ?? new Dictionary<string, string>();

        // 解析 worker 配置
        var workers = ParseWorkers(config);
        if (workers.Count == 0)
        {
            return new WorkflowNodeResult
            {
                Output = "No workers configured",
                IsSuccess = false,
                Error = "Parallel node requires 'workers' configuration"
            };
        }

        // 收集输入
        var inputText = WorkflowNodeHelper.CollectInput(context);
        inputText = state.ResolveTemplate(inputText);

        // 并行执行所有 worker，每个 worker 独立创建作用域避免共享 Scoped 服务（如 DbContext）
        var tasks = workers.Select(async worker =>
        {
            try
            {
                using var workerScope = _nodeContext.CreateScope();

                var workerConfig = new WorkflowNodeHelper.RefHolder
                {
                    Provider = step.Provider,
                    Model = step.Model,
                    Instructions = step.Instructions
                };

                // 如果 worker 指定了 agentId，从 worker scope 获取 AgentRepository 加载 Agent 定义
                // 注意：不能使用 _nodeContext.AgentRepository，因为并行线程共享外层 DbContext 不安全
                if (worker.AgentId.HasValue)
                {
                    var workerAgentRepo = workerScope.ServiceProvider.GetService<IRepository<Agent, Guid>>();
                    if (workerAgentRepo != null)
                    {
                        var agent = await workerAgentRepo.GetAsync(worker.AgentId.Value, cancellationToken);
                        if (agent != null)
                        {
                            workerConfig.Provider ??= agent.Provider;
                            workerConfig.Model ??= agent.Model;
                            workerConfig.Instructions ??= agent.Instructions;
                        }
                    }
                }

                // 从 worker scope 获取 AgentFactory 以隔离 Scoped 依赖（如 DbContext）
                var workerAgentFactory = workerScope.ServiceProvider.GetRequiredService<IAgentFactory>();
                var executor = await workerAgentFactory.CreateAgentAsync(
                    providerName: workerConfig.Provider,
                    model: workerConfig.Model,
                    instructions: workerConfig.Instructions,
                    name: worker.StepId,
                    agentId: worker.AgentId,
                    ct: cancellationToken);

                var messages = new List<ChatMessage> { new(ChatRole.User, inputText) };
                var response = await executor.ExecuteAsync(messages, cancellationToken);

                return (workerId: worker.StepId, text: response.Text ?? string.Empty, usage: response.Usage, error: (string?)null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Parallel worker '{WorkerId}' failed", worker.StepId);
                return (workerId: worker.StepId, text: $"[Error: {ex.Message}]", usage: (TokenUsageDto?)null, error: ex.Message);
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // 合并输出
        var sb = new StringBuilder();
        int totalPrompt = 0, totalCompletion = 0;
        var metadata = new Dictionary<string, string>();

        foreach (var (workerId, text, usage, error) in results)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"[{workerId}]");
            sb.AppendLine(text);

            metadata[$"worker_{workerId}"] = error ?? "success";

            if (usage != null)
            {
                totalPrompt += usage.InputTokens;
                totalCompletion += usage.OutputTokens;
            }
        }

        return new WorkflowNodeResult
        {
            Output = new WorkflowStepOutput
            {
                Text = sb.ToString().TrimEnd(),
                Metadata = metadata
            },
            Usage = totalPrompt > 0 || totalCompletion > 0
                ? new TokenUsageDto
                {
                    InputTokens = totalPrompt,
                    OutputTokens = totalCompletion,
                    TotalTokens = totalPrompt + totalCompletion
                }
                : null,
            IsSuccess = results.All(r => r.error == null)
        };
    }

    /// <summary>
    /// 解析 worker 配置
    /// </summary>
    private static List<WorkerConfig> ParseWorkers(Dictionary<string, string> config)
    {
        if (!config.TryGetValue("workers", out var workersJson) || string.IsNullOrWhiteSpace(workersJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<WorkerConfig>>(workersJson, TnziJsonDefaults.Options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Worker 配置（内部类型）
    /// </summary>
    private sealed class WorkerConfig
    {
        [JsonPropertyName("stepId")]
        public string StepId { get; set; } = string.Empty;

        [JsonPropertyName("agentId")]
        public Guid? AgentId { get; set; }
    }
}
