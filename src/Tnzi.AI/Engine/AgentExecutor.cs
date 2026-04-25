
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
    /// <summary>
    /// GracefulShutdown 模式下，外部取消后给工具的宽限时间
    /// </summary>
    public static readonly TimeSpan GracefulShutdownGracePeriod = TimeSpan.FromSeconds(5);

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
    /// 当前工具列表（只读）
    /// </summary>
    public IReadOnlyList<AITool> Tools => _options.Tools?.AsReadOnly() ?? (IReadOnlyList<AITool>)[];

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
            Middlewares = _options.Middlewares,
            ToolDefinitions = _options.ToolDefinitions
        };

        return new AgentExecutor(_chatClient, newOptions);
    }

    /// <summary>
    /// 创建一个使用指定工具列表的新 AgentExecutor（替换原有工具，不修改原实例）
    /// </summary>
    public AgentExecutor WithFilteredTools(IEnumerable<AITool> tools)
    {
        var newOptions = new AgentExecutorOptions
        {
            Name = _options.Name,
            Instructions = _options.Instructions,
            Tools = tools.ToList(),
            Temperature = _options.Temperature,
            MaxOutputTokens = _options.MaxOutputTokens,
            MaxToolIterations = _options.MaxToolIterations,
            ToolTimeoutSeconds = _options.ToolTimeoutSeconds,
            HistoryReducer = _options.HistoryReducer,
            Middlewares = _options.Middlewares,
            ToolDefinitions = _options.ToolDefinitions
        };

        return new AgentExecutor(_chatClient, newOptions);
    }

    /// <summary>
    /// 非流式执行 — tool-calling loop. Context injection (RAG/Memory/Skills/Persona/Profile)
    /// is fully owned by ContextInjectionMiddleware (Order=400) which runs before this executor
    /// — AgentExecutor no longer calls IContextProvider directly. History reduction
    /// (PruneChatReducer / SummarizeChatReducer) still runs inline so configured reducers stay active.
    /// </summary>
    public async Task<AgentResponse> ExecuteAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        Check.NotNull(messages);

        // 历史压缩（如果 OptionsBuilder 配置了 reducer）
        messages = await ReduceHistoryAsync(messages, ct);

        // 构建 ChatOptions（输出合并后的完整工具列表）
        var chatOptions = BuildChatOptions(out var allTools);

        // 工具调用循环
        var totalInputTokens = 0;
        var totalOutputTokens = 0;

        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);

            // 累计 token 用量
            if (response.Usage != null)
            {
                totalInputTokens += (int)(response.Usage.InputTokenCount ?? 0);
                totalOutputTokens += (int)(response.Usage.OutputTokenCount ?? 0);
            }

            // 将助手回复添加到消息列表
            messages.AddRange(response.Messages);

            // 检查是否有工具调用
            var toolCalls = ExtractToolCalls(response.Messages);

            // When enabled, strip TextContent from assistant messages that contain tool calls.
            // DeepSeek handles content + tool_calls poorly — sending both causes token
            // fracturing (\n\n between every token) in the continuation.
            if (_options.StripTextFromToolCallMessages && toolCalls.Count > 0)
            {
                for (var i = messages.Count - response.Messages.Count; i < messages.Count; i++)
                {
                    var msg = messages[i];
                    if (msg.Role == ChatRole.Assistant && msg.Contents.OfType<FunctionCallContent>().Any())
                    {
                        var filtered = msg.Contents.Where(c => c is not TextContent).ToList();
                        messages[i] = new ChatMessage(ChatRole.Assistant, [.. filtered]);
                    }
                }
            }

            if (toolCalls.Count == 0)
            {
                // 无工具调用，执行结束
                return new AgentResponse
                {
                    Text = response.Text,
                    Usage = new TokenUsageDto
                    {
                        InputTokens = totalInputTokens,
                        OutputTokens = totalOutputTokens,
                        TotalTokens = totalInputTokens + totalOutputTokens
                    },
                    FinishReason = response.FinishReason?.ToString(),
                    Messages = messages,
                    Citations = null,
                    Reasoning = ExtractReasoning(response.Messages)
                };
            }

            // 执行工具并添加结果到消息
            var toolResults = await ExecuteToolCallsAsync(toolCalls, allTools, ct);
            messages.Add(new ChatMessage(ChatRole.Tool, [.. toolResults]));
        }

        // 达到最大迭代次数 — 附加提示告知用户 AI 因达到工具调用上限而停止
        var lastAssistantText = messages.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text;
        var truncationNotice = $"\n\n[System: Reached the maximum tool call limit ({_options.MaxToolIterations}). The response may be incomplete. Please try a more specific query.]";
        return new AgentResponse
        {
            Text = (lastAssistantText ?? "") + truncationNotice,
            Usage = new TokenUsageDto
            {
                InputTokens = totalInputTokens,
                OutputTokens = totalOutputTokens,
                TotalTokens = totalInputTokens + totalOutputTokens
            },
            FinishReason = FinishReasons.MaxToolIterations,
            Messages = messages,
            Citations = null,
            Reasoning = ExtractReasoning(messages)
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

        // 历史压缩（如果 OptionsBuilder 配置了 reducer）
        messages = await ReduceHistoryAsync(messages, ct);

        // 构建 ChatOptions（输出合并后的完整工具列表）
        // Context injection is owned by ContextInjectionMiddleware (already ran before us).
        var chatOptions = BuildChatOptions(out var allTools);

        // 工具调用循环（流式版本）
        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            // 收集流式响应
            var responseText = new StringBuilder();
            var accumulatedReasoning = new StringBuilder();
            var toolCallContents = new List<FunctionCallContent>();
            UsageDetails? usage = null;
            ChatFinishReason? finishReason = null;
            var assistantContents = new List<AIContent>();
            var toolCallSignaled = false;

            // Early tool call detection: when text stops but stream continues,
            // the LLM likely switched to function call argument generation.
            // MEAI buffers the entire function call internally and only yields
            // FunctionCallContent after all arguments are received, causing
            // 60-120s silence for large tool arguments. Detect the text→silence
            // transition and emit an early hint so the frontend can show feedback.
            var hasReceivedText = false;
            var noTextUpdateCount = 0;
            var earlyToolSignalSent = false;
            const int noTextThreshold = 3;

            await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                // 收集内容（TextReasoningContent 为合成类型，不加入 assistantContents）
                foreach (var content in update.Contents)
                {
                    // Reasoning chunks are synthetic — yield them but do not add to the assistant message history
                    if (content is TextReasoningContent reasoning)
                    {
                        accumulatedReasoning.Append(reasoning.Text);
                        if (!toolCallSignaled)
                            yield return new AgentStreamChunk { ReasoningText = reasoning.Text };
                        continue;
                    }

                    assistantContents.Add(content);

                    if (content is FunctionCallContent functionCall)
                    {
                        toolCallContents.Add(functionCall);
                        // Signal tool call once with tool names — any text already streamed is acceptable
                        if (!toolCallSignaled)
                        {
                            toolCallSignaled = true;
                            yield return new AgentStreamChunk
                            {
                                IsToolCall = true,
                                ToolCallNames = toolCallContents.Select(tc => tc.Name).ToList()
                            };
                        }
                    }
                }

                // Early tool call name detection from provider's raw streaming data.
                // The function name appears in the very first chunk that contains a tool call,
                // 60-120s BEFORE MEAI yields the complete FunctionCallContent.
                if (!toolCallSignaled && !earlyToolSignalSent)
                {
                    var toolNames = ExtractPartialToolCallNames(update);
                    if (toolNames.Count > 0)
                    {
                        earlyToolSignalSent = true;
                        yield return new AgentStreamChunk
                        {
                            IsToolCall = true,
                            ToolCallNames = toolNames
                        };
                    }
                }

                if (update.Text != null)
                {
                    responseText.Append(update.Text);
                    hasReceivedText = true;
                    noTextUpdateCount = 0;
                }
                else if (hasReceivedText && !toolCallSignaled && !earlyToolSignalSent)
                {
                    // Fallback: text was flowing but stopped — LLM may be generating function call arguments.
                    // After noTextThreshold consecutive updates with no text, emit an early hint.
                    // This covers non-OpenAI providers where RawRepresentation lacks ToolCallUpdates.
                    noTextUpdateCount++;
                    if (noTextUpdateCount >= noTextThreshold)
                    {
                        earlyToolSignalSent = true;
                        yield return new AgentStreamChunk { IsToolCall = true };
                    }
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
                // Always emit text — even in an iteration that also contains tool calls,
                // so the model can output a one-line acknowledgment before/alongside tool use.
                if (update.Text != null)
                {
                    yield return new AgentStreamChunk { Text = update.Text };
                }
            }

            // Fallback: deepseek-reasoner sometimes emits the final answer only inside reasoning_content
            // with empty content, leaving the user with a blank response. If no text content was streamed
            // but reasoning was accumulated and no tool calls were made, re-emit the reasoning as response text.
            if (responseText.Length == 0 && accumulatedReasoning.Length > 0 && toolCallContents.Count == 0)
            {
                var fallbackText = accumulatedReasoning.ToString();
                assistantContents.Add(new TextContent(fallbackText));
                responseText.Append(fallbackText);
                yield return new AgentStreamChunk { Text = fallbackText };
            }

            // 添加助手消息到历史
            // When enabled, exclude TextContent from the assistant message that has tool calls.
            // DeepSeek handles content + tool_calls poorly — sending both causes token
            // fracturing (\n\n between every token) in the continuation. The text was
            // already streamed to the client, so omitting it from the history is safe.
            var historyContents = _options.StripTextFromToolCallMessages && toolCallContents.Count > 0
                ? assistantContents.Where(c => c is not TextContent).ToList()
                : assistantContents;
            var assistantMessage = new ChatMessage(ChatRole.Assistant, [.. historyContents]);
            messages.Add(assistantMessage);

            // 检查是否有工具调用
            if (toolCallContents.Count == 0)
            {
                yield return new AgentStreamChunk
                {
                    Usage = ConvertUsage(usage),
                    FinishReason = finishReason?.ToString(),
                    Citations = null
                };
                yield break;
            }

            // Intermediate iteration with tool call — tool call indicator was already sent above.

            // 执行工具（含详情，用于客户端性能基准）
            var (toolResults, toolDetails) = await ExecuteToolCallsWithDetailsAsync(toolCallContents, allTools, ct);
            messages.Add(new ChatMessage(ChatRole.Tool, [.. toolResults]));

            // Emit tool call details for client-side benchmarking
            yield return new AgentStreamChunk { ToolCalls = toolDetails };

            // 注意: RecordToolCall 已在 ExecuteToolCallsCoreAsync 内部调用，此处不重复记录
        }

        // 达到最大迭代次数 — 发送提示文本告知用户
        yield return new AgentStreamChunk
        {
            Text = $"\n\n[System: Reached the maximum tool call limit ({_options.MaxToolIterations}). The response may be incomplete. Please try a more specific query.]"
        };
        yield return new AgentStreamChunk
        {
            FinishReason = FinishReasons.MaxToolIterations,
            Citations = null
        };
    }

    /// <summary>
    /// 压缩历史 — invokes the configured IHistoryReducer if any.
    /// </summary>
    private async Task<List<ChatMessage>> ReduceHistoryAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        if (_options.HistoryReducer == null)
        {
            return messages;
        }

        var reductionResult = await _options.HistoryReducer.ReduceAsync(messages, ct);

        if (reductionResult.OriginalMessageCount != reductionResult.ReducedMessageCount)
        {
            _options.Logger?.LogInformation(
                "History reduced by {Strategy}: {OriginalMessages}→{ReducedMessages} messages, ~{OriginalTokens}→{ReducedTokens} tokens ({TokensSaved} saved, {Ratio:P0} ratio)",
                reductionResult.StrategyName,
                reductionResult.OriginalMessageCount,
                reductionResult.ReducedMessageCount,
                reductionResult.EstimatedOriginalTokens,
                reductionResult.EstimatedReducedTokens,
                reductionResult.TokensSaved,
                reductionResult.CompressionRatio);
        }

        return reductionResult.Messages;
    }

    /// <summary>
    /// 构建 ChatOptions，同时输出合并后的完整工具列表。
    /// Tool injection from ContextProvider is now handled at the runtime layer
    /// (AgentRuntime.MergeAdditionalTools) before this executor is called.
    /// </summary>
    private ChatOptions BuildChatOptions(out IList<AITool> allTools)
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

        var tools = _options.Tools is { Count: > 0 } ? new List<AITool>(_options.Tools) : new List<AITool>();
        if (tools.Count > 0)
        {
            chatOptions.Tools = tools;
        }

        allTools = tools;
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
    /// 并行执行工具调用（非流式路径，不需要详情）
    /// </summary>
    private async Task<List<FunctionResultContent>> ExecuteToolCallsAsync(List<FunctionCallContent> toolCalls, IList<AITool> allTools, CancellationToken ct)
    {
        var (results, _) = await ExecuteToolCallsCoreAsync(toolCalls, allTools, ct);
        return results;
    }

    /// <summary>
    /// 并行执行工具调用并返回详情（流式路径，包含名称、耗时、成功状态）
    /// </summary>
    private Task<(List<FunctionResultContent> Results, List<ToolCallDetail> Details)> ExecuteToolCallsWithDetailsAsync(
        List<FunctionCallContent> toolCalls, IList<AITool> allTools, CancellationToken ct)
        => ExecuteToolCallsCoreAsync(toolCalls, allTools, ct);

    /// <summary>
    /// 执行工具调用核心实现（条件并行/串行 + 中间件管道 + 增强追踪 + 空结果标记）
    /// </summary>
    /// <remarks>
    /// 安全规则（借鉴 Claude Code StreamingToolExecutor）：
    /// 仅当所有工具都是 IsConcurrencySafe 且无 IsDestructive 时才并行执行，否则降级为顺序执行。
    /// </remarks>
    private async Task<(List<FunctionResultContent> Results, List<ToolCallDetail> Details)> ExecuteToolCallsCoreAsync(
        List<FunctionCallContent> toolCalls, IList<AITool> allTools, CancellationToken ct)
    {
        // 安全守卫：检查是否可以并发执行
        var toolNames = toolCalls.Select(tc => tc.Name).ToList();
        if (toolCalls.Count > 1 && !CanExecuteConcurrently(toolNames, _options.ToolDefinitions))
        {
            // 降级为顺序执行
            var seqResults = new List<FunctionResultContent>();
            var seqDetails = new List<ToolCallDetail>();
            foreach (var toolCall in toolCalls)
            {
                var (result, detail) = await ExecuteSingleToolCallAsync(toolCall, allTools, ct);
                seqResults.Add(result);
                seqDetails.Add(detail);
            }
            return (seqResults, seqDetails);
        }

        // 并行路径（所有工具都安全时保持不变）
        var tasks = toolCalls.Select(tc => ExecuteSingleToolCallAsync(tc, allTools, ct));
        var pairs = await Task.WhenAll(tasks);
        return ([.. pairs.Select(p => p.Item1)], [.. pairs.Select(p => p.Item2)]);
    }

    /// <summary>
    /// 执行单个工具调用（从 ExecuteToolCallsCoreAsync lambda 提取，供串行/并行路径复用）
    /// </summary>
    private async Task<(FunctionResultContent Result, ToolCallDetail Detail)> ExecuteSingleToolCallAsync(
        FunctionCallContent toolCall, IList<AITool> allTools, CancellationToken ct)
    {
        // 从合并后的完整工具列表中查找（包含 ContextProvider 注入的工具）
        var tool = FindTool(toolCall.Name, allTools);
        if (tool == null)
        {
            var detail = new ToolCallDetail { Name = toolCall.Name, IsSuccess = false, Error = "Tool not found" };
            return (new FunctionResultContent(toolCall.CallId, $"Tool '{toolCall.Name}' not found"), detail);
        }

        // 构建参数摘要（用于追踪，截取前 200 字符避免大参数污染 trace）
        var argSummary = toolCall.Arguments is { Count: > 0 }
            ? string.Join(", ", toolCall.Arguments.Select(kv => $"{kv.Key}={Truncate(kv.Value?.ToString(), 50)}"))
            : null;
        if (argSummary?.Length > 200) argSummary = argSummary[..200] + "...";

        ToolDefinition? toolDef = null;
        var interruptBehavior = ToolInterruptBehavior.Cancel;
        if (_options.ToolDefinitions?.TryGetValue(toolCall.Name, out toolDef) == true)
        {
            interruptBehavior = toolDef.InterruptBehavior;
        }

        var toolContext = new ToolExecutionContext
        {
            ToolName = toolCall.Name,
            ToolGroup = toolDef?.GroupName,
            CallId = toolCall.CallId,
            Arguments = toolCall.Arguments,
            CancellationToken = ct
        };

        try
        {
            using var activity = AIActivitySource.StartToolActivity(toolCall.Name, argumentSummary: argSummary);
            var sw = Stopwatch.StartNew();

            // 构建核心执行委托（根据 ToolInterruptBehavior 决定取消策略）
            Func<Task<object?>> coreExecution = async () =>
            {
                if (tool is not AIFunction aiFunction)
                {
                    throw new InvalidOperationException($"Tool '{toolCall.Name}' is not invocable");
                }

                var args = toolCall.Arguments is null ? null : new AIFunctionArguments(toolCall.Arguments);

                if (interruptBehavior == ToolInterruptBehavior.GracefulShutdown)
                {
                    // GracefulShutdown: 不直接链接外部 CT（链接会立即传播取消），
                    // 而是用独立 CTS + 回调延迟取消，给工具宽限期完成清理。
                    using var graceCts = new CancellationTokenSource();
                    using var registration = ct.Register(() => graceCts.CancelAfter(GracefulShutdownGracePeriod));

                    if (ct.IsCancellationRequested)
                    {
                        // 外部已取消 → 启动宽限倒计时
                        graceCts.CancelAfter(GracefulShutdownGracePeriod);
                    }

                    return await aiFunction.InvokeAsync(args, graceCts.Token).AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(_options.ToolTimeoutSeconds), graceCts.Token);
                }

                // Cancel (默认): 直接传播取消信号
                return await aiFunction.InvokeAsync(args, ct).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(_options.ToolTimeoutSeconds), ct);
            };

            // 通过中间件管道执行
            var result = await ExecuteWithMiddlewareAsync(toolContext, coreExecution);

            // 空结果标记注入（防止 LLM 误以为调用失败）
            var normalizedResult = NormalizeToolResult(toolCall.Name, result);
            var toolFailure = ResolveToolFailure(toolContext);
            var isSuccess = string.IsNullOrWhiteSpace(toolFailure);

            sw.Stop();
            var resultSummary = Truncate(normalizedResult?.ToString(), 200);
            AIActivitySource.RecordToolCallDetailed(toolCall.Name, sw.Elapsed.TotalSeconds, resultSummary: resultSummary, isSuccess: isSuccess);
            AIActivitySource.CompleteToolActivity(activity, sw.Elapsed.TotalSeconds, resultSummary, isSuccess);

            var detail = new ToolCallDetail
            {
                Name = toolCall.Name,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                IsSuccess = isSuccess,
                Error = toolFailure
            };
            return (new FunctionResultContent(toolCall.CallId, normalizedResult), detail);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // 传播取消信号，不吞掉
        }
        catch (TimeoutException)
        {
            var detail = new ToolCallDetail
            {
                Name = toolCall.Name,
                DurationMs = _options.ToolTimeoutSeconds * 1000,
                IsSuccess = false,
                Error = $"Timed out after {_options.ToolTimeoutSeconds}s"
            };
            return (new FunctionResultContent(toolCall.CallId, $"Tool '{toolCall.Name}' timed out after {_options.ToolTimeoutSeconds} seconds"), detail);
        }
        catch (Exception ex)
        {
            AIActivitySource.RecordToolCallDetailed(toolCall.Name, 0, isSuccess: false);
            _options.Logger?.LogError(ex, "Tool '{ToolName}' execution failed", toolCall.Name);
            var detail = new ToolCallDetail { Name = toolCall.Name, IsSuccess = false, Error = ex.Message };
            return (new FunctionResultContent(toolCall.CallId, $"Tool execution failed: {ex.Message}"), detail);
        }
    }

    /// <summary>
    /// 通过中间件管道执行工具调用
    /// </summary>
    private async Task<object?> ExecuteWithMiddlewareAsync(ToolExecutionContext context, Func<Task<object?>> coreExecution)
    {
        var middlewares = _options.Middlewares;
        if (middlewares == null || middlewares.Count == 0)
        {
            return await coreExecution();
        }

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

    private static string? ResolveToolFailure(ToolExecutionContext context)
    {
        return ToolErrorRecoveryMiddleware.GetRecoveredError(context)
            ?? ToolGuardrailMiddleware.GetDenialReason(context)
            ?? RequiresSkillToolMiddleware.GetShortCircuitReason(context);
    }

    /// <summary>
    /// 从消息列表中提取推理内容
    /// </summary>
    private static string? ExtractReasoning(IEnumerable<ChatMessage> messages)
    {
        var reasoningParts = new List<string>();
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                {
                    reasoningParts.Add(reasoning.Text);
                }
            }
        }
        return reasoningParts.Count > 0 ? string.Join("", reasoningParts) : null;
    }

    /// <summary>
    /// 判断一组工具是否可以安全并发执行。
    /// 仅当所有工具都是 IsConcurrencySafe 且无 IsDestructive 时才并行。
    /// 单个工具始终返回 true（无需并发判断）。
    /// 未知工具 → fail-closed → 返回 false。
    /// </summary>
    public static bool CanExecuteConcurrently(
        IReadOnlyList<string> toolNames,
        IReadOnlyDictionary<string, ToolDefinition>? toolDefinitions)
    {
        if (toolNames.Count <= 1) return true;
        if (toolDefinitions == null || toolDefinitions.Count == 0) return false;

        foreach (var name in toolNames)
        {
            if (!toolDefinitions.TryGetValue(name, out var toolDef))
                return false; // 未知工具 → fail-closed
            if (!toolDef.IsConcurrencySafe)
                return false;
            if (toolDef.IsDestructive)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 标准化工具结果 — 空结果添加标记，防止 LLM 误以为调用失败。
    /// </summary>
    public static object NormalizeToolResult(string toolName, object? result)
    {
        if (result is null or "" or " ")
            return $"({toolName} completed with no output)";
        if (result is string s && string.IsNullOrWhiteSpace(s))
            return $"({toolName} completed with no output)";
        return result;
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
    /// Extract function names from partial tool call data in the raw streaming update.
    /// Works with OpenAI-compatible providers (DeepSeek, OpenAI, etc.) whose
    /// RawRepresentation is OpenAI.Chat.StreamingChatCompletionUpdate with ToolCallUpdates.
    /// Uses reflection to avoid hard dependency on the OpenAI SDK package.
    /// </summary>
    private static List<string> ExtractPartialToolCallNames(ChatResponseUpdate update)
    {
        var names = new List<string>();
        try
        {
            var raw = update.RawRepresentation;
            if (raw == null) return names;

            var toolCallUpdatesProperty = raw.GetType().GetProperty("ToolCallUpdates");
            if (toolCallUpdatesProperty?.GetValue(raw) is not System.Collections.IEnumerable toolCallUpdates)
                return names;

            foreach (var toolCallUpdate in toolCallUpdates)
            {
                var fnNameProp = toolCallUpdate.GetType().GetProperty("FunctionName");
                if (fnNameProp?.GetValue(toolCallUpdate) is string fnName && !string.IsNullOrEmpty(fnName))
                {
                    names.Add(fnName);
                }
            }
        }
        catch
        {
            // Silently ignore — this is a best-effort optimization
        }
        return names;
    }

    /// <summary>
    /// 将 MEAI UsageDetails 转换为 TokenUsageDto（引擎层出口转换）
    /// </summary>
    private static TokenUsageDto? ConvertUsage(UsageDetails? usage)
    {
        if (usage == null) return null;
        var dto = new TokenUsageDto
        {
            InputTokens = (int)(usage.InputTokenCount ?? 0),
            OutputTokens = (int)(usage.OutputTokenCount ?? 0),
            TotalTokens = (int)(usage.TotalTokenCount ?? 0)
        };

        // Extract cache metrics from AdditionalCounts (provider-specific fields)
        // OpenAI: "CachedInputTokens" / Anthropic: "cache_creation_input_tokens", "cache_read_input_tokens"
        // DeepSeek: "prompt_cache_hit_tokens" (via OpenAI-compatible API)
        if (usage.AdditionalCounts is { Count: > 0 })
        {
            // OpenAI-style (Microsoft.Extensions.AI normalizes to this)
            if (usage.AdditionalCounts.TryGetValue("CachedInputTokens", out var cached))
                dto.CachedInputTokens = (int)cached;

            // Anthropic-style
            if (usage.AdditionalCounts.TryGetValue("cache_read_input_tokens", out var cacheRead))
                dto.CachedInputTokens = (int)cacheRead;
            if (usage.AdditionalCounts.TryGetValue("cache_creation_input_tokens", out var cacheCreate))
                dto.CacheCreationTokens = (int)cacheCreate;

            // DeepSeek-style (OpenAI-compatible, may pass through as-is)
            if (usage.AdditionalCounts.TryGetValue("prompt_cache_hit_tokens", out var dsCache))
                dto.CachedInputTokens = (int)dsCache;
        }

        return dto;
    }

}
