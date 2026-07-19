namespace Tnzi.AI.Workflow.Engine.Nodes;

/// <summary>
/// 审批节点 — 通过通用中断机制暂停工作流，等待人工审批（ResumeWithInputAsync）
/// </summary>
/// <remarks>
/// 此节点使用 CheckInterruptAsync 返回 Approval 类型中断。
/// 恢复时通过 ResumeData 传入 approved(bool) 和 comment(string) 字段。
/// </remarks>
public class ApprovalNode : IWorkflowNode
{
    private readonly ILogger<ApprovalNode> _logger;

    public string NodeType => WorkflowNodeTypes.Approval;

    public ApprovalNode(ILogger<ApprovalNode> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 检查是否需要中断等待审批
    /// </summary>
    public Task<WorkflowInterrupt?> CheckInterruptAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var approvalContent = WorkflowNodeHelper.CollectInput(context);

        var interrupt = new WorkflowInterrupt
        {
            StepId = step.StepId ?? "unknown",
            Reason = $"Approval required for step '{step.StepId}'. Content: {(approvalContent.Length > 200 ? approvalContent[..200] + "..." : approvalContent)}",
            Type = InterruptType.Approval,
            RequestedInput = new Dictionary<string, object>
            {
                ["approved"] = "bool: whether to approve (true) or reject (false)",
                ["comment"] = "string: optional approval/rejection comment"
            }
        };

        _logger.LogInformation("Approval node '{StepId}' requesting interrupt for human approval", step.StepId);

        return Task.FromResult<WorkflowInterrupt?>(interrupt);
    }

    /// <summary>
    /// 执行审批节点（仅在恢复后调用，读取 ResumeData 中的审批结果）
    /// </summary>
    public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var approvalContent = WorkflowNodeHelper.CollectInput(context);

        // 从 ResumeData 读取审批结果
        if (context.IsResuming && context.ResumeData != null)
        {
            var approved = context.ResumeData.TryGetValue("approved", out var approvedObj)
                && approvedObj is bool approvedBool
                    ? approvedBool
                    : context.ResumeData.TryGetValue("approved", out var approvedStr)
                      && string.Equals(approvedStr?.ToString(), "true", StringComparison.OrdinalIgnoreCase);

            var comment = context.ResumeData.TryGetValue("comment", out var commentObj)
                ? commentObj?.ToString() ?? string.Empty
                : string.Empty;

            if (approved)
            {
                _logger.LogInformation("Approval node '{StepId}' approved with comment: {Comment}", step.StepId, comment);

                return Task.FromResult(new WorkflowNodeResult
                {
                    Output = new WorkflowStepOutput
                    {
                        Text = string.IsNullOrWhiteSpace(comment) ? approvalContent : comment,
                        Metadata = new Dictionary<string, string>
                        {
                            ["status"] = "approved",
                            ["step_id"] = step.StepId ?? "unknown",
                            ["comment"] = comment
                        }
                    },
                    IsSuccess = true
                });
            }

            _logger.LogInformation("Approval node '{StepId}' rejected: {Comment}", step.StepId, comment);

            return Task.FromResult(new WorkflowNodeResult
            {
                Output = new WorkflowStepOutput
                {
                    Text = $"[Rejected: {comment}]",
                    Metadata = new Dictionary<string, string>
                    {
                        ["status"] = "rejected",
                        ["step_id"] = step.StepId ?? "unknown",
                        ["comment"] = comment
                    }
                },
                IsSuccess = false,
                Error = $"Approval rejected: {comment}"
            });
        }

        // 非恢复状态下的 fallback（不应到达此处，因为 CheckInterruptAsync 会先拦截）
        _logger.LogWarning("Approval node '{StepId}' executed without resume data, returning awaiting status", step.StepId);

        return Task.FromResult(new WorkflowNodeResult
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
        });
    }
}
