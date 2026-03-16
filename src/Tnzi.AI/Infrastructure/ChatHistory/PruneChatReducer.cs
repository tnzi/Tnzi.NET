namespace Tnzi.AI.Infrastructure.ChatHistory;

/// <summary>
/// 裁剪式聊天消息压缩器 - 保留最近的消息轮数
/// </summary>
/// <remarks>
/// <para>
/// 实现 IHistoryReducer 接口，通过裁剪旧消息来控制上下文窗口大小。
/// </para>
/// <para>
/// 裁剪策略：
/// <list type="bullet">
/// <item><description>保留最近 N 轮对话（用户消息 + 助手回复算一轮）</description></item>
/// <item><description>可选择性地更激进裁剪旧的工具输出</description></item>
/// <item><description>可设置受保护的工具名称，不裁剪其输出</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class PruneChatReducer : IHistoryReducer
{
    private readonly PruneOptions _options;
    private readonly ILogger<PruneChatReducer> _logger;

    /// <summary>
    /// 初始化 PruneChatReducer
    /// </summary>
    /// <param name="options">裁剪配置选项</param>
    /// <param name="logger">日志记录器</param>
    public PruneChatReducer(PruneOptions options, ILogger<PruneChatReducer> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 裁剪消息列表
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>裁剪后的消息列表</returns>
    public Task<List<ChatMessage>> ReduceAsync(
        List<ChatMessage> messages,
        CancellationToken ct = default)
    {
        var originalCount = messages.Count;

        if (originalCount == 0)
        {
            return Task.FromResult(messages);
        }

        // 计算要保留的消息
        var result = PruneMessages(messages);

        if (result.Count < originalCount)
        {
            _logger.LogDebug(
                "Pruned chat history from {OriginalCount} to {NewCount} messages (KeepLastTurns={KeepLastTurns})",
                originalCount, result.Count, _options.KeepLastTurns);
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// 执行消息裁剪
    /// </summary>
    private List<ChatMessage> PruneMessages(List<ChatMessage> messages)
    {
        // 步骤 1：找出所有系统消息（始终保留）
        var systemMessages = messages.Where(m => m.Role == ChatRole.System).ToList();

        // 步骤 2：获取非系统消息
        var nonSystemMessages = messages.Where(m => m.Role != ChatRole.System).ToList();

        // 步骤 3：计算对话轮数并保留最近 N 轮
        // 一轮定义为：用户消息 + 后续的助手/工具消息
        var turns = ChatHistoryHelper.GroupMessagesByTurns(nonSystemMessages);

        var keepTurns = _options.KeepLastTurns;
        var turnsToKeep = turns.Count > keepTurns ? turns.Skip(turns.Count - keepTurns).ToList() : turns;

        // 步骤 4：建立 CallId → ToolName 映射表（从 assistant 消息的 FunctionCallContent 中提取）
        var callIdToToolName = ChatHistoryHelper.BuildCallIdToToolNameMap(turnsToKeep.SelectMany(t => t));

        // 步骤 5：对保留的轮次应用工具输出裁剪（如果配置了）
        var keptMessages = new List<ChatMessage>();
        for (int i = 0; i < turnsToKeep.Count; i++)
        {
            var turn = turnsToKeep[i];
            var turnAge = turnsToKeep.Count - i; // 轮次年龄（从最新开始计数）

            foreach (var message in turn)
            {
                // 检查是否需要裁剪工具输出
                if (ShouldPruneToolOutput(message, turnAge, callIdToToolName))
                {
                    _logger.LogTrace("Pruning tool output from turn {TurnAge}", turnAge);
                    continue; // 跳过此消息
                }

                keptMessages.Add(message);
            }
        }

        // 步骤 6：合并系统消息和保留的消息
        var result = new List<ChatMessage>(systemMessages.Count + keptMessages.Count);
        result.AddRange(systemMessages);
        result.AddRange(keptMessages);

        return result;
    }

    /// <summary>
    /// 判断是否应该裁剪工具输出
    /// </summary>
    private bool ShouldPruneToolOutput(ChatMessage message, int turnAge, Dictionary<string, string> callIdToToolName)
    {
        // 如果没有配置工具输出裁剪阈值，不裁剪
        if (!_options.DropToolOutputsOlderThan.HasValue)
        {
            return false;
        }

        // 只处理工具消息
        if (message.Role != ChatRole.Tool)
        {
            return false;
        }

        // 检查轮次年龄是否超过阈值
        if (turnAge <= _options.DropToolOutputsOlderThan.Value)
        {
            return false;
        }

        // 检查是否为受保护的工具（通过 CallId → ToolName 映射查找真实工具名）
        if (_options.ProtectedTools.Count > 0)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionResultContent functionResult && functionResult.CallId != null)
                {
                    if (callIdToToolName.TryGetValue(functionResult.CallId, out var toolName))
                    {
                        if (_options.ProtectedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }
}
