
namespace Tnzi.AI.Infrastructure.Engine;

/// <summary>
/// 工作流执行器 — 支持顺序和并行两种 Agent 编排模式
/// </summary>
/// <remarks>
/// <para>
/// 提供顺序和并行两种执行模式。每个 Agent 接收前一个 Agent 的输出（顺序模式）或全部接收相同输入（并行模式）。
/// </para>
/// </remarks>
public static class WorkflowRunner
{
    /// <summary>
    /// 顺序执行多个 Agent（链式传递）
    /// </summary>
    /// <param name="agents">要执行的 Agent 列表（按顺序）</param>
    /// <param name="input">初始输入</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>最终 Agent 的执行结果</returns>
    public static async Task<AgentResponse> RunSequentialAsync(IReadOnlyList<AgentExecutor> agents, string input, CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(agents);
        Check.NotNullOrEmpty(input);

        var currentInput = input;
        AgentResponse? lastResponse = null;

        for (var i = 0; i < agents.Count; i++)
        {
            var agent = agents[i];
            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, currentInput)
            };

            lastResponse = await agent.ExecuteAsync(messages, ct);

            // 将当前 Agent 的输出作为下一个 Agent 的输入
            currentInput = lastResponse.Text ?? string.Empty;
        }

        return lastResponse ?? new AgentResponse { Text = string.Empty };
    }

    /// <summary>
    /// 并行执行多个 Agent（相同输入，合并输出）
    /// </summary>
    /// <param name="agents">要执行的 Agent 列表</param>
    /// <param name="input">共享输入</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>合并后的执行结果</returns>
    public static async Task<AgentResponse> RunParallelAsync(IReadOnlyList<AgentExecutor> agents, string input, CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(agents);
        Check.NotNullOrEmpty(input);

        // 并行执行所有 Agent
        var tasks = agents.Select(agent =>
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, input)
            };
            return agent.ExecuteAsync(messages, ct);
        }).ToList();

        var responses = await Task.WhenAll(tasks);

        // 合并结果
        var combinedText = new StringBuilder();
        long totalInputTokens = 0;
        long totalOutputTokens = 0;

        for (var i = 0; i < responses.Length; i++)
        {
            var response = responses[i];
            var agentName = agents[i].Name;

            if (!string.IsNullOrEmpty(response.Text))
            {
                if (combinedText.Length > 0) combinedText.AppendLine();
                combinedText.AppendLine($"[{agentName}]");
                combinedText.AppendLine(response.Text);
            }

            if (response.Usage != null)
            {
                totalInputTokens += response.Usage.InputTokenCount ?? 0;
                totalOutputTokens += response.Usage.OutputTokenCount ?? 0;
            }
        }

        return new AgentResponse
        {
            Text = combinedText.ToString().TrimEnd(),
            Usage = new UsageDetails
            {
                InputTokenCount = totalInputTokens,
                OutputTokenCount = totalOutputTokens,
                TotalTokenCount = totalInputTokens + totalOutputTokens
            },
            FinishReason = "stop"
        };
    }
}
