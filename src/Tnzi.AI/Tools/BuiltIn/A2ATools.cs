namespace Tnzi.AI.Tools.BuiltIn;

/// <summary>
/// A2A (Agent-to-Agent) 工具 - 让 LLM 调用远程 Agent 完成任务
/// </summary>
[AIToolGroup("a2a", "Agent-to-Agent", "Invoke remote AI agents via A2A protocol")]
public class A2ATools : IAIToolProvider
{
    private readonly ILogger _logger;
    private readonly IA2AClient? _client;

    /// <summary>
    /// 轮询间隔（毫秒），默认 5000ms。
    /// </summary>
    public int PollIntervalMs { get; init; } = DefaultPollIntervalMs;

    private const int MaxPollAttempts = 60;
    private const int DefaultPollIntervalMs = 5000;

    public A2ATools(ILogger<A2ATools> logger, IA2AClient? client = null)
    {
        _logger = Check.NotNull(logger);
        _client = client;
    }

    /// <summary>
    /// Invoke a remote AI agent via A2A protocol to complete a task.
    /// </summary>
    [AIFunction("invoke_a2a_agent", "Send a task to a remote AI agent and wait for the result")]
    public async Task<string> InvokeAgentAsync(
        [AIParameter("endpoint", "The remote agent's base URL (e.g., https://agent.example.com)")] string endpoint,
        [AIParameter("prompt", "The task description to send to the remote agent")] string prompt,
        CancellationToken ct = default)
    {
        if (_client == null)
            return "A2A client is unavailable. No remote agent service is configured.";

        try
        {
            var request = new A2ATaskRequest { Input = prompt };
            _logger.LogDebug("Sending A2A task to {Endpoint}: {TaskId}", endpoint, request.TaskId);

            var response = await _client.SendTaskAsync(endpoint, request, ct);

            if (response.Status is "completed")
                return response.Output ?? "(no output)";

            if (response.Status is "failed")
                return $"Remote agent task failed: {response.Error ?? "unknown error"}";

            // 轮询等待 pending/running 状态
            for (var i = 0; i < MaxPollAttempts; i++)
            {
                await Task.Delay(PollIntervalMs, ct);

                var status = await _client.GetTaskStatusAsync(endpoint, response.TaskId, ct);

                if (status.Status is "completed")
                    return status.Output ?? "(no output)";

                if (status.Status is "failed")
                    return $"Remote agent task failed: {status.Error ?? "unknown error"}";
            }

            return $"Remote agent task timed out after {MaxPollAttempts * PollIntervalMs / 1000}s (task: {response.TaskId})";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A2A invoke error for {Endpoint}", endpoint);
            return $"A2A invoke error: {ex.Message}";
        }
    }
}
