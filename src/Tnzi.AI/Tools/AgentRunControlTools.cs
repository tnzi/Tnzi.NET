namespace Tnzi.AI.Tools;

/// <summary>
/// Agent runtime 控制工具
/// </summary>
/// <remarks>
/// 整组由 <c>ai.tools.task</c> 门住：这些工具能起后台运行、读别人的运行摘要、取消或向其送输入，
/// 不该是任何登录用户自选一个工具组就能拿到的能力。
/// </remarks>
[AIToolGroup("task", RequiredPermissions = AIToolPermissions.Task)]
public class AgentRunControlTools : IAIToolProvider
{
    private readonly IAgentRuntimeControlService _controlService;

    public AgentRunControlTools(IAgentRuntimeControlService controlService)
    {
        _controlService = Check.NotNull(controlService);
    }

    /// <summary>
    /// Spawn a tracked background agent run using an existing agent or sub-agent type template.
    /// </summary>
    [AIFunction("spawn_agent",
        "Spawn a tracked background agent run. Use agentId to run an existing agent, or subAgentType to launch a template-defined sub-agent.")]
    public async Task<string> SpawnAgentAsync(
        [Description("Initial user message for the spawned run")] string message,
        [Description("Optional existing agent ID")] Guid? agentId = null,
        [Description("Optional sub-agent type template name")] string? subAgentType = null,
        [Description("Optional provider override")] string? provider = null,
        [Description("Optional model override")] string? model = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _controlService.SpawnAsync(new SpawnAgentRunInput
        {
            Message = message,
            AgentId = agentId,
            SubAgentType = subAgentType,
            Provider = provider,
            Model = model
        }, cancellationToken);

        return result.Succeeded && result.Data != null
            ? result.Data.ToJsonString(camelCase: true)
            : result.Message ?? "Failed to spawn agent";
    }

    /// <summary>
    /// Get the current state of a tracked agent run.
    /// </summary>
    [AIFunction("get_agent_run",
        "Get the current state of a tracked agent run, including whether it is waiting for approval or extra input.")]
    public async Task<string> GetAgentRunAsync(
        [Description("Tracked agent run ID")] Guid runId,
        CancellationToken cancellationToken = default)
    {
        var result = await _controlService.GetStateAsync(runId, AgentRunAccessScope.Caller, cancellationToken);
        return result.Succeeded && result.Data != null
            ? result.Data.ToJsonString(camelCase: true)
            : result.Message ?? "Run not found";
    }

    /// <summary>
    /// Wait until a tracked agent run reaches a stable observable state.
    /// </summary>
    [AIFunction("wait_agent",
        "Wait until a tracked agent run completes, fails, is cancelled, or pauses for approval/input.")]
    public async Task<string> WaitAgentAsync(
        [Description("Tracked agent run ID")] Guid runId,
        [Description("Maximum seconds to wait before returning the latest state")] int timeoutSeconds = 30,
        [Description("Polling interval in milliseconds")] int pollIntervalMs = 1000,
        CancellationToken cancellationToken = default)
    {
        var result = await _controlService.WaitAsync(runId, new WaitAgentRunInput
        {
            TimeoutSeconds = timeoutSeconds,
            PollIntervalMs = pollIntervalMs
        }, AgentRunAccessScope.Caller, cancellationToken);

        return result.Succeeded && result.Data != null
            ? result.Data.ToJsonString(camelCase: true)
            : result.Message ?? "Failed to wait for run";
    }

    /// <summary>
    /// Send additional input to a paused or failed agent run and resume it.
    /// </summary>
    [AIFunction("send_agent_input",
        "Send extra input to a paused agent run and attempt to resume it. Use workflowInput for structured workflow interrupts.")]
    public async Task<string> SendAgentInputAsync(
        [Description("Tracked agent run ID")] Guid runId,
        [Description("Free-form message for the resumed run")] string? message = null,
        [Description("Workflow interrupt step ID when resuming a workflow waiting for input")] string? workflowStepId = null,
        [Description("Structured workflow input payload keyed by field name")] Dictionary<string, object>? workflowInput = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _controlService.SendInputAsync(runId, new SendAgentRunInput
        {
            Message = message,
            WorkflowStepId = workflowStepId,
            WorkflowInput = workflowInput
        }, AgentRunAccessScope.Caller, cancellationToken);

        return result.Succeeded && result.Data != null
            ? result.Data.ToJsonString(camelCase: true)
            : result.Message ?? "Failed to send input";
    }

    /// <summary>
    /// Cancel a tracked agent run.
    /// </summary>
    [AIFunction("kill_agent",
        "Cancel a tracked agent run that is still active or waiting for approval/input.")]
    public async Task<string> KillAgentAsync(
        [Description("Tracked agent run ID")] Guid runId,
        CancellationToken cancellationToken = default)
    {
        var result = await _controlService.KillAsync(runId, AgentRunAccessScope.Caller, cancellationToken);
        return result.Succeeded ? $"Run {runId} cancelled." : result.Message ?? "Failed to cancel run";
    }

    /// <summary>
    /// List recent agent runs, optionally filtered by status.
    /// </summary>
    [AIFunction("list_agent_runs",
        "List recent agent runs ordered by creation time (newest first). Optionally filter by status (Pending, Running, Completed, Failed, Cancelled, AwaitingApproval, RequiresClarification).")]
    public async Task<string> ListAgentRunsAsync(
        [Description("Maximum number of results to return (1-100)")] int maxResults = 20,
        [Description("Optional status filter (e.g. Running, Completed, Failed)")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        AgentRunStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AgentRunStatus>(status, ignoreCase: true, out var parsed))
        {
            statusFilter = parsed;
        }

        var result = await _controlService.ListRunsAsync(maxResults, statusFilter, AgentRunAccessScope.Caller, cancellationToken);
        return result.Succeeded && result.Data != null
            ? result.Data.ToJsonString(camelCase: true)
            : result.Message ?? "Failed to list agent runs";
    }

    /// <summary>
    /// List available sub-agent type templates.
    /// </summary>
    [AIFunction("list_sub_agent_types",
        "List registered sub-agent type templates, including tool groups, turn limits, and capability tags.")]
    public async Task<string> ListSubAgentTypesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _controlService.ListSubAgentTypesAsync(cancellationToken);
        return result.Succeeded && result.Data != null
            ? result.Data.ToJsonString(camelCase: true)
            : result.Message ?? "Failed to list sub-agent types";
    }
}
