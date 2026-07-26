
namespace Tnzi.AI.Engine.GroupChat;

/// <summary>
/// GroupChat 编排器 - 多 Agent 轮流讨论
/// </summary>
/// <remarks>
/// <para>
/// 多个 Agent 按选择策略轮流发言，共享对话历史。
/// 支持三种选择策略：轮询(RoundRobin)、随机(Random)、LLM 智能选择(LLMSelector)。
/// </para>
/// <para>
/// 终止条件：达到最大轮次、检测到终止关键词、或自定义终止判断。
/// </para>
/// </remarks>
public class GroupChatOrchestrator
{
    private readonly List<IAgentExecutor> _agents = [];
    private readonly ILogger _logger;

    public GroupChatOrchestrator(ILogger logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// GroupChat 配置
    /// </summary>
    public GroupChatOptions Options { get; set; } = new();

    /// <summary>
    /// 添加参与讨论的 Agent
    /// </summary>
    public GroupChatOrchestrator AddAgent(IAgentExecutor agent)
    {
        Check.NotNull(agent);
        _agents.Add(agent);
        return this;
    }

    /// <summary>
    /// 运行 GroupChat
    /// </summary>
    /// <param name="topic">讨论主题/初始输入</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>GroupChat 执行结果</returns>
    public async Task<GroupChatResult> RunAsync(string topic, CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(topic);

        if (_agents.Count < 2)
        {
            throw new InvalidOperationException("GroupChat requires at least 2 agents");
        }

        var history = new List<GroupChatMessage>();
        var sharedMessages = new List<ChatMessage>
        {
            new(ChatRole.User, topic)
        };
        var citations = new List<CitationDto>();
        int totalInputTokens = 0;
        int totalOutputTokens = 0;

        var currentAgentIndex = 0;
        var lastSpeakerName = string.Empty;

        for (var round = 0; round < Options.MaxRounds; round++)
        {
            // 选择下一个发言的 Agent
            var agent = SelectNextAgent(round, currentAgentIndex, lastSpeakerName, history);

            // 为当前 Agent 构建带上下文的消息列表
            var agentMessages = BuildAgentMessages(agent, sharedMessages, history);

            try
            {
                var response = await agent.ExecuteAsync(agentMessages, ct);
                var responseText = response.Text ?? string.Empty;

                if (response.Usage != null)
                {
                    totalInputTokens += response.Usage.InputTokens;
                    totalOutputTokens += response.Usage.OutputTokens;
                }

                if (response.Citations is { Count: > 0 })
                {
                    citations.AddRange(response.Citations);
                }

                // 记录到历史
                history.Add(new GroupChatMessage
                {
                    AgentName = agent.Name,
                    Content = responseText,
                    Round = round + 1
                });

                // 添加到共享消息列表
                sharedMessages.Add(new ChatMessage(ChatRole.Assistant, $"[{agent.Name}]: {responseText}"));

                _logger.LogDebug("GroupChat round {Round}: Agent '{AgentName}' responded", round + 1, agent.Name);

                lastSpeakerName = agent.Name;

                // 检查终止条件
                if (ShouldTerminate(responseText, history))
                {
                    _logger.LogDebug("GroupChat terminated at round {Round}: termination condition met", round + 1);
                    break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent '{AgentName}' failed in round {Round}", agent.Name, round + 1);
                history.Add(new GroupChatMessage
                {
                    AgentName = agent.Name,
                    Content = $"[Error: {ex.Message}]",
                    Round = round + 1
                });
            }

            // 推进到下一个 Agent（轮询模式）
            currentAgentIndex = (currentAgentIndex + 1) % _agents.Count;
        }

        // 合并所有讨论内容
        var combinedOutput = new StringBuilder();
        foreach (var msg in history)
        {
            combinedOutput.AppendLine($"[{msg.AgentName}] (Round {msg.Round}):");
            combinedOutput.AppendLine(msg.Content);
            combinedOutput.AppendLine();
        }

        return new GroupChatResult
        {
            Output = combinedOutput.ToString().TrimEnd(),
            History = history,
            TotalRounds = history.Count > 0 ? history[^1].Round : 0,
            ConversationMessages = sharedMessages,
            Citations = citations.Count > 0 ? citations : null,
            Usage = totalInputTokens > 0 || totalOutputTokens > 0
                ? new TokenUsageDto
                {
                    InputTokens = totalInputTokens,
                    OutputTokens = totalOutputTokens,
                    TotalTokens = totalInputTokens + totalOutputTokens
                }
                : null
        };
    }

    /// <summary>
    /// 选择下一个发言的 Agent
    /// </summary>
    private IAgentExecutor SelectNextAgent(int round, int currentIndex, string lastSpeaker, List<GroupChatMessage> history)
    {
        return Options.SelectionStrategy switch
        {
            GroupChatSelectionStrategy.RoundRobin => _agents[currentIndex],
            GroupChatSelectionStrategy.Random => _agents[Random.Shared.Next(_agents.Count)],
            GroupChatSelectionStrategy.Custom when Options.CustomSelector != null =>
                Options.CustomSelector(_agents, history, lastSpeaker) ?? _agents[currentIndex],
            _ => _agents[currentIndex]
        };
    }

    /// <summary>
    /// 为 Agent 构建带上下文的消息列表
    /// </summary>
    private static List<ChatMessage> BuildAgentMessages(IAgentExecutor agent, List<ChatMessage> sharedMessages, List<GroupChatMessage> history)
    {
        var messages = new List<ChatMessage>(sharedMessages);

        // 添加讨论上下文提示
        if (history.Count > 0)
        {
            messages.Add(new ChatMessage(ChatRole.System,
                $"You are '{agent.Name}' participating in a group discussion. " +
                $"Other agents have already shared their perspectives above. " +
                $"Please provide your unique contribution. Be concise and avoid repeating what others have said."));
        }

        return messages;
    }

    /// <summary>
    /// 检查是否应终止讨论
    /// </summary>
    private bool ShouldTerminate(string lastResponse, List<GroupChatMessage> history)
    {
        // 检查终止关键词
        if (Options.TerminationKeywords.Count > 0)
        {
            foreach (var keyword in Options.TerminationKeywords)
            {
                if (lastResponse.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // 检查自定义终止条件
        if (Options.TerminationCondition != null)
        {
            return Options.TerminationCondition(history);
        }

        return false;
    }
}

/// <summary>
/// GroupChat 配置选项
/// </summary>
public class GroupChatOptions
{
    /// <summary>最大讨论轮次</summary>
    public int MaxRounds { get; set; } = 10;

    /// <summary>Agent 选择策略</summary>
    public GroupChatSelectionStrategy SelectionStrategy { get; set; } = GroupChatSelectionStrategy.RoundRobin;

    /// <summary>终止关键词列表（任一出现则终止讨论）</summary>
    public List<string> TerminationKeywords { get; set; } = ["TERMINATE", "DONE", "CONSENSUS"];

    /// <summary>自定义终止条件</summary>
    public Func<List<GroupChatMessage>, bool>? TerminationCondition { get; set; }

    /// <summary>自定义 Agent 选择器</summary>
    public Func<List<IAgentExecutor>, List<GroupChatMessage>, string, IAgentExecutor?>? CustomSelector { get; set; }
}

/// <summary>
/// Agent 选择策略
/// </summary>
public enum GroupChatSelectionStrategy
{
    /// <summary>轮询（按注册顺序循环）</summary>
    RoundRobin,
    /// <summary>随机选择</summary>
    Random,
    /// <summary>自定义选择器</summary>
    Custom
}

/// <summary>
/// GroupChat 中的单条消息
/// </summary>
public class GroupChatMessage
{
    /// <summary>发言 Agent 名称</summary>
    public string AgentName { get; set; } = string.Empty;
    /// <summary>消息内容</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>讨论轮次</summary>
    public int Round { get; set; }
}

/// <summary>
/// GroupChat 执行结果
/// </summary>
public class GroupChatResult
{
    /// <summary>合并的讨论输出</summary>
    public string Output { get; set; } = string.Empty;
    /// <summary>完整讨论历史</summary>
    public List<GroupChatMessage> History { get; set; } = [];
    /// <summary>总讨论轮次</summary>
    public int TotalRounds { get; set; }
    /// <summary>可持久化的对话消息</summary>
    public List<ChatMessage> ConversationMessages { get; set; } = [];
    /// <summary>聚合 Token 用量</summary>
    public TokenUsageDto? Usage { get; set; }
    /// <summary>聚合引用来源</summary>
    public List<CitationDto>? Citations { get; set; }
}
