
using Tnzi.AI.Infrastructure.Observability;

namespace Tnzi.AI.Infrastructure.Engine;

/// <summary>
/// Agent 执行器 — 基于 MEAI IChatClient 的工具调用循环引擎
/// </summary>
/// <remarks>
/// <para>
/// 核心循环：调用 LLM → 检测 FunctionCallContent → 执行工具 → 回送 FunctionResultContent → 继续循环。
/// 支持非流式和流式两种模式，集成 ContextProvider 和 HistoryReducer。
/// </para>
/// </remarks>
public class AgentExecutor
{
    private readonly IChatClient _chatClient;
    private readonly AgentExecutorOptions _options;

    public AgentExecutor(IChatClient chatClient, AgentExecutorOptions options)
    {
        _chatClient = Check.NotNull(chatClient);
        _options = Check.NotNull(options);
    }

    /// <summary>
    /// Agent 名称
    /// </summary>
    public string Name => _options.Name;

    /// <summary>
    /// 非流式执行
    /// </summary>
    public async Task<AgentResponse> ExecuteAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        Check.NotNull(messages);

        // 1. 上下文注入
        var contextInjection = await InjectContextAsync(messages, ct);

        // 2. 历史压缩
        messages = await ReduceHistoryAsync(messages, ct);

        // 3. 构建 ChatOptions
        var chatOptions = BuildChatOptions(contextInjection);

        // 4. 工具调用循环
        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);

            // 将助手回复添加到消息列表
            messages.AddRange(response.Messages);

            // 检查是否有工具调用
            var toolCalls = ExtractToolCalls(response.Messages);
            if (toolCalls.Count == 0)
            {
                // 无工具调用，执行结束
                await NotifyContextCompletedAsync(messages, ct);

                return new AgentResponse
                {
                    Text = response.Text,
                    Usage = response.Usage,
                    FinishReason = response.FinishReason?.ToString(),
                    Messages = messages
                };
            }

            // 执行工具并添加结果到消息
            var toolResults = await ExecuteToolCallsAsync(toolCalls, ct);
            messages.Add(new ChatMessage(ChatRole.Tool, [.. toolResults]));
        }

        // 达到最大迭代次数，返回最后的消息
        await NotifyContextCompletedAsync(messages, ct);

        var lastAssistantText = messages.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text;
        return new AgentResponse
        {
            Text = lastAssistantText,
            FinishReason = "max_tool_iterations",
            Messages = messages
        };
    }

    /// <summary>
    /// 流式执行
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(
        List<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        Check.NotNull(messages);

        // 1. 上下文注入
        var contextInjection = await InjectContextAsync(messages, ct);

        // 2. 历史压缩
        messages = await ReduceHistoryAsync(messages, ct);

        // 3. 构建 ChatOptions
        var chatOptions = BuildChatOptions(contextInjection);

        // 4. 工具调用循环（流式版本）
        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            // 收集流式响应
            var responseText = new StringBuilder();
            var toolCallContents = new List<FunctionCallContent>();
            UsageDetails? usage = null;
            ChatFinishReason? finishReason = null;
            var assistantContents = new List<AIContent>();

            await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                // 收集内容
                foreach (var content in update.Contents)
                {
                    assistantContents.Add(content);

                    if (content is FunctionCallContent functionCall)
                    {
                        toolCallContents.Add(functionCall);
                    }
                }

                if (update.Text != null)
                {
                    responseText.Append(update.Text);
                }

                // 提取 Usage
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent && usageContent.Details != null)
                    {
                        usage = usageContent.Details;
                    }
                }

                finishReason = update.FinishReason ?? finishReason;

                // 仅当无工具调用时才 yield 文本 chunk
                if (update.Text != null && toolCallContents.Count == 0)
                {
                    yield return new AgentStreamChunk { Text = update.Text };
                }
            }

            // 添加助手消息到历史
            var assistantMessage = new ChatMessage(ChatRole.Assistant, [.. assistantContents]);
            messages.Add(assistantMessage);

            // 检查是否有工具调用
            if (toolCallContents.Count == 0)
            {
                // 无工具调用，流式结束
                await NotifyContextCompletedAsync(messages, ct);

                yield return new AgentStreamChunk
                {
                    Usage = usage,
                    FinishReason = finishReason?.ToString()
                };
                yield break;
            }

            // 有工具调用，通知客户端
            yield return new AgentStreamChunk { IsToolCall = true };

            // 执行工具
            var toolResults = await ExecuteToolCallsAsync(toolCallContents, ct);
            messages.Add(new ChatMessage(ChatRole.Tool, [.. toolResults]));

            // 记录工具调用指标
            foreach (var tc in toolCallContents)
            {
                AIActivitySource.RecordToolCall(tc.Name);
            }
        }

        // 达到最大迭代次数
        await NotifyContextCompletedAsync(messages, ct);

        yield return new AgentStreamChunk
        {
            FinishReason = "max_tool_iterations"
        };
    }

    /// <summary>
    /// 注入上下文
    /// </summary>
    private async Task<ContextInjection> InjectContextAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        if (_options.ContextProvider == null)
        {
            return ContextInjection.Empty;
        }

        var injection = await _options.ContextProvider.GetContextAsync(messages, ct);

        if (injection.Messages != null && injection.Messages.Count > 0)
        {
            // 在消息列表头部插入上下文消息（系统消息之后）
            var insertIndex = messages.FindIndex(m => m.Role != ChatRole.System);
            if (insertIndex < 0) insertIndex = messages.Count;
            messages.InsertRange(insertIndex, injection.Messages);
        }

        return injection;
    }

    /// <summary>
    /// 压缩历史
    /// </summary>
    private async Task<List<ChatMessage>> ReduceHistoryAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        if (_options.HistoryReducer == null)
        {
            return messages;
        }

        return await _options.HistoryReducer.ReduceAsync(messages, ct);
    }

    /// <summary>
    /// 构建 ChatOptions
    /// </summary>
    private ChatOptions BuildChatOptions(ContextInjection? contextInjection)
    {
        var chatOptions = new ChatOptions();

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
        {
            chatOptions.Instructions = _options.Instructions;
        }

        if (_options.Temperature.HasValue)
        {
            chatOptions.Temperature = _options.Temperature.Value;
        }

        if (_options.MaxOutputTokens.HasValue)
        {
            chatOptions.MaxOutputTokens = _options.MaxOutputTokens.Value;
        }

        // 合并工具：Options 中的工具 + ContextProvider 注入的工具
        var allTools = new List<AITool>();
        if (_options.Tools != null)
        {
            allTools.AddRange(_options.Tools);
        }
        if (contextInjection?.Tools != null)
        {
            allTools.AddRange(contextInjection.Tools);
        }
        if (allTools.Count > 0)
        {
            chatOptions.Tools = allTools;
        }

        return chatOptions;
    }

    /// <summary>
    /// 从助手消息中提取工具调用
    /// </summary>
    private static List<FunctionCallContent> ExtractToolCalls(IList<ChatMessage> messages)
    {
        var result = new List<FunctionCallContent>();
        foreach (var msg in messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fcc)
                {
                    result.Add(fcc);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 执行工具调用
    /// </summary>
    private async Task<List<FunctionResultContent>> ExecuteToolCallsAsync(List<FunctionCallContent> toolCalls, CancellationToken ct)
    {
        var results = new List<FunctionResultContent>();

        foreach (var toolCall in toolCalls)
        {
            // 记录工具调用指标
            AIActivitySource.RecordToolCall(toolCall.Name);

            // 查找工具
            var tool = FindTool(toolCall.Name);
            if (tool == null)
            {
                results.Add(new FunctionResultContent(toolCall.CallId, $"Tool '{toolCall.Name}' not found"));
                continue;
            }

            try
            {
                using var activity = AIActivitySource.StartToolActivity(toolCall.Name);

                // 执行工具（AIFunction 支持直接调用）
                if (tool is AIFunction aiFunction)
                {
                    var args = toolCall.Arguments is null ? null : new AIFunctionArguments(toolCall.Arguments);
                    var result = await aiFunction.InvokeAsync(args, ct);
                    results.Add(new FunctionResultContent(toolCall.CallId, result));

                    AIActivitySource.CompleteActivity(activity);
                }
                else
                {
                    results.Add(new FunctionResultContent(toolCall.CallId, $"Tool '{toolCall.Name}' is not invocable"));
                }
            }
            catch (Exception ex)
            {
                results.Add(new FunctionResultContent(toolCall.CallId, $"Tool execution failed: {ex.Message}"));
            }
        }

        return results;
    }

    /// <summary>
    /// 查找工具
    /// </summary>
    private AITool? FindTool(string name)
    {
        if (_options.Tools == null) return null;

        foreach (var tool in _options.Tools)
        {
            if (string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return tool;
            }
        }

        return null;
    }

    /// <summary>
    /// 通知 ContextProvider 执行完成
    /// </summary>
    private async Task NotifyContextCompletedAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        if (_options.ContextProvider != null)
        {
            await _options.ContextProvider.OnCompletedAsync(messages, ct);
        }
    }
}
