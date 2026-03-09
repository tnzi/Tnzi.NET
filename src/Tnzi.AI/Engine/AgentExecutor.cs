
namespace Tnzi.AI.Engine;

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
    /// 创建一个包含额外工具的新 AgentExecutor（不修改原实例）
    /// </summary>
    public AgentExecutor WithAdditionalTools(IEnumerable<AITool> additionalTools)
    {
        var mergedTools = new List<AITool>();
        if (_options.Tools != null) mergedTools.AddRange(_options.Tools);
        mergedTools.AddRange(additionalTools);

        var newOptions = new AgentExecutorOptions
        {
            Name = _options.Name,
            Instructions = _options.Instructions,
            Tools = mergedTools,
            Temperature = _options.Temperature,
            MaxOutputTokens = _options.MaxOutputTokens,
            MaxToolIterations = _options.MaxToolIterations,
            ToolTimeoutSeconds = _options.ToolTimeoutSeconds,
            HistoryReducer = _options.HistoryReducer,
            ContextProvider = _options.ContextProvider,
            Middlewares = _options.Middlewares
        };

        return new AgentExecutor(_chatClient, newOptions);
    }

    /// <summary>
    /// 非流式执行
    /// </summary>
    public async Task<AgentResponse> ExecuteAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        Check.NotNull(messages);

        // 1. 历史压缩（先压缩，避免后续注入的 RAG 上下文被裁剪）
        messages = await ReduceHistoryAsync(messages, ct);

        // 2. 上下文注入
        var contextInjection = await InjectContextAsync(messages, ct);

        // 3. 构建 ChatOptions（输出合并后的完整工具列表）
        var chatOptions = BuildChatOptions(contextInjection, out var allTools);

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
                    Usage = ConvertUsage(response.Usage),
                    FinishReason = response.FinishReason?.ToString(),
                    Messages = messages,
                    Citations = contextInjection.Citations
                };
            }

            // 执行工具并添加结果到消息
            var toolResults = await ExecuteToolCallsAsync(toolCalls, allTools, ct);
            messages.Add(new ChatMessage(ChatRole.Tool, [.. toolResults]));
        }

        // 达到最大迭代次数，返回最后的消息
        await NotifyContextCompletedAsync(messages, ct);

        var lastAssistantText = messages.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text;
        return new AgentResponse
        {
            Text = lastAssistantText,
            FinishReason = "max_tool_iterations",
            Messages = messages,
            Citations = contextInjection.Citations
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

        // 1. 历史压缩（先压缩，避免后续注入的 RAG 上下文被裁剪）
        messages = await ReduceHistoryAsync(messages, ct);

        // 2. 上下文注入
        var contextInjection = await InjectContextAsync(messages, ct);

        // 3. 构建 ChatOptions（输出合并后的完整工具列表）
        var chatOptions = BuildChatOptions(contextInjection, out var allTools);

        // 4. 工具调用循环（流式版本）
        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            // 收集流式响应
            var responseText = new StringBuilder();
            var toolCallContents = new List<FunctionCallContent>();
            UsageDetails? usage = null;
            ChatFinishReason? finishReason = null;
            var assistantContents = new List<AIContent>();
            var toolCallSignaled = false;

            await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                // 收集内容（TextReasoningContent 为合成类型，不加入 assistantContents）
                foreach (var content in update.Contents)
                {
                    // Reasoning chunks are synthetic — yield them but do not add to the assistant message history
                    if (content is TextReasoningContent reasoning)
                    {
                        if (!toolCallSignaled)
                            yield return new AgentStreamChunk { ReasoningText = reasoning.Text };
                        continue;
                    }

                    assistantContents.Add(content);

                    if (content is FunctionCallContent functionCall)
                    {
                        toolCallContents.Add(functionCall);
                        // Signal tool call once — any text already streamed is acceptable
                        if (!toolCallSignaled)
                        {
                            toolCallSignaled = true;
                            yield return new AgentStreamChunk { IsToolCall = true };
                        }
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

                // Stream text chunks immediately for real-time typewriter effect.
                // Skip text after a tool call has been detected (intermediate iteration).
                if (update.Text != null && !toolCallSignaled)
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
                await NotifyContextCompletedAsync(messages, ct);

                yield return new AgentStreamChunk
                {
                    Usage = ConvertUsage(usage),
                    FinishReason = finishReason?.ToString(),
                    Citations = contextInjection.Citations
                };
                yield break;
            }

            // Intermediate iteration with tool call — tool call indicator was already sent above.

            // 执行工具
            var toolResults = await ExecuteToolCallsAsync(toolCallContents, allTools, ct);
            messages.Add(new ChatMessage(ChatRole.Tool, [.. toolResults]));

            // 注意: RecordToolCall 已在 ExecuteToolCallsAsync 内部调用，此处不重复记录
        }

        // 达到最大迭代次数
        await NotifyContextCompletedAsync(messages, ct);

        yield return new AgentStreamChunk
        {
            FinishReason = "max_tool_iterations",
            Citations = contextInjection.Citations
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
    /// 构建 ChatOptions，同时输出合并后的完整工具列表
    /// </summary>
    private ChatOptions BuildChatOptions(ContextInjection? contextInjection, out IList<AITool> allTools)
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
        var mergedTools = new List<AITool>();
        if (_options.Tools != null)
        {
            mergedTools.AddRange(_options.Tools);
        }
        if (contextInjection?.Tools != null)
        {
            mergedTools.AddRange(contextInjection.Tools);
        }
        if (mergedTools.Count > 0)
        {
            chatOptions.Tools = mergedTools;
        }

        allTools = mergedTools;
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
    /// 并行执行工具调用（支持中间件管道和增强追踪）
    /// </summary>
    private async Task<List<FunctionResultContent>> ExecuteToolCallsAsync(List<FunctionCallContent> toolCalls, IList<AITool> allTools, CancellationToken ct)
    {
        var tasks = toolCalls.Select(async toolCall =>
        {
            // 从合并后的完整工具列表中查找（包含 ContextProvider 注入的工具）
            var tool = FindTool(toolCall.Name, allTools);
            if (tool == null)
            {
                return new FunctionResultContent(toolCall.CallId, $"Tool '{toolCall.Name}' not found");
            }

            // 构建参数摘要（用于追踪，截取前 200 字符避免大参数污染 trace）
            var argSummary = toolCall.Arguments is { Count: > 0 }
                ? string.Join(", ", toolCall.Arguments.Select(kv => $"{kv.Key}={Truncate(kv.Value?.ToString(), 50)}"))
                : null;
            if (argSummary?.Length > 200) argSummary = argSummary[..200] + "...";

            try
            {
                using var activity = AIActivitySource.StartToolActivity(toolCall.Name, argumentSummary: argSummary);
                var sw = Stopwatch.StartNew();

                // 构建核心执行委托
                Func<Task<object?>> coreExecution = async () =>
                {
                    if (tool is not AIFunction aiFunction)
                    {
                        throw new InvalidOperationException($"Tool '{toolCall.Name}' is not invocable");
                    }

                    var args = toolCall.Arguments is null ? null : new AIFunctionArguments(toolCall.Arguments);
                    return await aiFunction.InvokeAsync(args, ct).AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(_options.ToolTimeoutSeconds), ct);
                };

                // 通过中间件管道执行
                var result = await ExecuteWithMiddlewareAsync(toolCall, coreExecution, ct);

                sw.Stop();
                var resultSummary = Truncate(result?.ToString(), 200);
                AIActivitySource.RecordToolCallDetailed(toolCall.Name, sw.Elapsed.TotalSeconds, resultSummary: resultSummary);
                AIActivitySource.CompleteToolActivity(activity, sw.Elapsed.TotalSeconds, resultSummary);

                return new FunctionResultContent(toolCall.CallId, result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // 传播取消信号，不吞掉
            }
            catch (TimeoutException)
            {
                return new FunctionResultContent(toolCall.CallId, $"Tool '{toolCall.Name}' timed out after {_options.ToolTimeoutSeconds} seconds");
            }
            catch (Exception ex)
            {
                AIActivitySource.RecordToolCallDetailed(toolCall.Name, 0, isSuccess: false);
                _options.Logger?.LogError(ex, "Tool '{ToolName}' execution failed", toolCall.Name);
                return new FunctionResultContent(toolCall.CallId, $"Tool execution failed: {ex.Message}");
            }
        });

        var results = await Task.WhenAll(tasks);
        return [.. results];
    }

    /// <summary>
    /// 通过中间件管道执行工具调用
    /// </summary>
    private async Task<object?> ExecuteWithMiddlewareAsync(FunctionCallContent toolCall, Func<Task<object?>> coreExecution, CancellationToken ct)
    {
        var middlewares = _options.Middlewares;
        if (middlewares == null || middlewares.Count == 0)
        {
            return await coreExecution();
        }

        // 构建中间件上下文
        var context = new ToolExecutionContext
        {
            ToolName = toolCall.Name,
            CallId = toolCall.CallId,
            Arguments = toolCall.Arguments,
            CancellationToken = ct
        };

        // 构建洋葱模型调用链（从最后一个中间件向第一个包装）
        var pipeline = coreExecution;
        for (var i = middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = middlewares[i];
            var next = pipeline;
            pipeline = () => middleware.InvokeAsync(context, next);
        }

        return await pipeline();
    }

    /// <summary>
    /// 截取字符串（用于追踪摘要）
    /// </summary>
    private static string? Truncate(string? value, int maxLength)
    {
        if (value == null || value.Length <= maxLength) return value;
        return value[..maxLength] + "...";
    }

    /// <summary>
    /// 从合并后的完整工具列表中查找工具
    /// </summary>
    private static AITool? FindTool(string name, IList<AITool> allTools)
    {
        foreach (var tool in allTools)
        {
            if (string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return tool;
            }
        }

        return null;
    }

    /// <summary>
    /// 将 MEAI UsageDetails 转换为 TokenUsageDto（引擎层出口转换）
    /// </summary>
    private static TokenUsageDto? ConvertUsage(UsageDetails? usage)
    {
        if (usage == null) return null;
        return new TokenUsageDto
        {
            PromptTokens = (int)(usage.InputTokenCount ?? 0),
            CompletionTokens = (int)(usage.OutputTokenCount ?? 0),
            TotalTokens = (int)(usage.TotalTokenCount ?? 0)
        };
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
