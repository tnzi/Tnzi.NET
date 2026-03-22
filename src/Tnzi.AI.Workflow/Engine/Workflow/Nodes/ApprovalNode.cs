namespace Tnzi.AI.Engine.Workflow.Nodes;

/// <summary>
/// 审批节点 — 保存检查点后暂停工作流，等待人工审批（ResumeAsync）
/// </summary>
/// <remarks>
/// 此节点将上游输出传递到检查点存储，然后返回 AwaitingApproval=true。
/// 工作流引擎检测到此标志后暂停执行，等待外部调用 ResumeAsync 恢复。
/// </remarks>
public class ApprovalNode : IWorkflowNode
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApprovalNode> _logger;

    public string NodeType => WorkflowNodeTypes.Approval;

    public ApprovalNode(IServiceProvider serviceProvider, ILogger<ApprovalNode> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;

        // 收集上游输出作为审批内容
        var approvalContent = CollectUpstreamOutputs(context);

        _logger.LogInformation("Approval node '{StepId}' awaiting human approval", step.StepId);

        var result = new WorkflowNodeResult
        {
            Output = new WorkflowStepOutput
            {
                Text = approvalContent,
                Metadata = new Dictionary<string, string>
                {
                    ["status"] = "awaiting_approval",
                    ["step_id"] = step.StepId ?? "unknown"
                }
            },
            IsSuccess = true,
            AwaitingApproval = true
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// 收集上游依赖输出
    /// </summary>
    private static string CollectUpstreamOutputs(WorkflowNodeContext context)
    {
        if (context.DependencyOutputs.Count == 0)
            return context.State.InitialInput;

        if (context.DependencyOutputs.Count == 1)
            return context.DependencyOutputs.Values.First().Text;

        var sb = new StringBuilder();
        foreach (var (depId, output) in context.DependencyOutputs)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"[{depId}]");
            sb.AppendLine(output.Text);
        }

        return sb.ToString().TrimEnd();
    }
}
