
namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 执行器 - 基于 MEAI IChatClient 的工具调用循环引擎
/// </summary>
/// <remarks>
/// <para>
/// 核心循环：调用 LLM → 检测 FunctionCallContent → 执行工具 → 回送 FunctionResultContent → 继续循环。
/// 支持非流式和流式两种模式，集成 ContextProvider 和 HistoryReducer。
/// </para>
/// </remarks>
public class AgentExecutor : IAgentExecutor
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
    /// 创建一个包含额外工具的新 AgentExecutor（不修改原实例）。
    /// 选项经 <see cref="AgentExecutorOptions.Clone"/> 全量复制（含 Logger、StripTextFromToolCallMessages 等）。
    /// </summary>
    public AgentExecutor WithAdditionalTools(IEnumerable<AITool> additionalTools)
    {
        var mergedTools = new List<AITool>();
        if (_options.Tools != null) mergedTools.AddRange(_options.Tools);
        mergedTools.AddRange(additionalTools);

        var newOptions = _options.Clone();
        newOptions.Tools = mergedTools;

        return new AgentExecutor(_chatClient, newOptions);
    }

    IAgentExecutor IAgentExecutor.WithAdditionalTools(IEnumerable<AITool> additionalTools)
        => WithAdditionalTools(additionalTools);

    /// <summary>
    /// 创建一个去掉指定名称工具的新 AgentExecutor（不修改原实例，名称不区分大小写）。
    /// </summary>
    public AgentExecutor WithoutTools(IEnumerable<string> toolNames)
    {
        var excluded = toolNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (excluded.Count == 0 || _options.Tools is not { Count: > 0 })
            return this;

        var kept = _options.Tools.Where(t => t.Name == null || !excluded.Contains(t.Name)).ToList();
        return kept.Count == _options.Tools.Count ? this : WithFilteredTools(kept);
    }

    IAgentExecutor IAgentExecutor.WithoutTools(IEnumerable<string> toolNames)
        => WithoutTools(toolNames);

    /// <summary>
    /// 创建一个换用指定系统指令的新 AgentExecutor（不修改原实例）。
    /// </summary>
    public AgentExecutor WithInstructions(string instructions)
    {
        Check.NotNullOrWhiteSpace(instructions);

        var newOptions = _options.Clone();
        newOptions.Instructions = instructions;
        return new AgentExecutor(_chatClient, newOptions);
    }

    IAgentExecutor IAgentExecutor.WithInstructions(string instructions)
        => WithInstructions(instructions);

    /// <summary>
    /// 创建一个使用指定工具循环上限的新 AgentExecutor（不修改原实例）。
    /// </summary>
    public AgentExecutor WithMaxToolIterations(int maxToolIterations)
    {
        Check.Positive(maxToolIterations);

        var newOptions = _options.Clone();
        newOptions.MaxToolIterations = maxToolIterations;
        return new AgentExecutor(_chatClient, newOptions);
    }

    IAgentExecutor IAgentExecutor.WithMaxToolIterations(int maxToolIterations)
        => WithMaxToolIterations(maxToolIterations);

    /// <summary>
    /// 创建一个使用指定工具列表的新 AgentExecutor（替换原有工具，不修改原实例）。
    /// 选项经 <see cref="AgentExecutorOptions.Clone"/> 全量复制。
    /// </summary>
    public AgentExecutor WithFilteredTools(IEnumerable<AITool> tools)
    {
        var newOptions = _options.Clone();
        newOptions.Tools = tools.ToList();

        return new AgentExecutor(_chatClient, newOptions);
    }

    /// <summary>
    /// 非流式执行 - tool-calling loop. Context injection (RAG/Memory/Skills/Persona/Profile)
    /// is fully owned by ContextInjectionMiddleware (Order=400) which runs before this executor
    /// - AgentExecutor no longer calls IContextProvider directly. History reduction
    /// (PruneChatReducer / SummarizeChatReducer) still runs inline so configured reducers stay active.
    /// </summary>
    public async Task<AgentResponse> ExecuteAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        Check.NotNull(messages);

        // 工作副本 - 不修改调用方传入的列表（不变性规则）
        var working = new List<ChatMessage>(messages);

        // 历史压缩（如果 OptionsBuilder 配置了 reducer）
        working = await ReduceHistoryAsync(working, ct);

        // 构建 ChatOptions（输出合并后的完整工具列表）
        var (chatOptions, allTools) = BuildChatOptions();

        // 工具调用循环
        TokenUsageDto? totalUsage = null;

        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            var response = await _chatClient.GetResponseAsync(working, chatOptions, ct);

            // 累计 token 用量，与流式路径同一换算（含缓存命中 / 写入；此前非流式只累计输入输出，缓存数恒为 0）
            totalUsage = StreamUsage.Add(totalUsage, ConvertUsage(response.Usage));

            // 将助手回复添加到工作副本
            working.AddRange(response.Messages);

            // 检查是否有工具调用
            var toolCalls = ExtractToolCalls(response.Messages);

            // When enabled, strip TextContent from assistant messages that contain tool calls.
            // DeepSeek handles content + tool_calls poorly - sending both causes token
            // fracturing (\n\n between every token) in the continuation.
            if (_options.StripTextFromToolCallMessages && toolCalls.Count > 0)
            {
                for (var i = working.Count - response.Messages.Count; i < working.Count; i++)
                {
                    var msg = working[i];
                    if (msg.Role == ChatRole.Assistant && msg.Contents.OfType<FunctionCallContent>().Any())
                    {
                        var filtered = msg.Contents.Where(c => c is not TextContent).ToList();
                        working[i] = new ChatMessage(ChatRole.Assistant, [.. filtered]);
                    }
                }
            }

            if (toolCalls.Count == 0)
            {
                // 无工具调用，执行结束
                return new AgentResponse
                {
                    Text = response.Text,
                    Usage = totalUsage ?? new TokenUsageDto(),
                    FinishReason = response.FinishReason?.ToString(),
                    Messages = working,
                    Citations = null,
                    Reasoning = ExtractReasoning(response.Messages)
                };
            }

            // 执行工具并添加结果到工作副本
            var toolResults = await ExecuteToolCallsAsync(toolCalls, allTools, ct);
            working.Add(new ChatMessage(ChatRole.Tool, [.. toolResults]));
        }

        // 达到最大迭代次数 - 附加提示告知用户 AI 因达到工具调用上限而停止
        var lastAssistantText = working.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text;
        var truncationNotice = $"\n\n[System: Reached the maximum tool call limit ({_options.MaxToolIterations}). The response may be incomplete. Please try a more specific query.]";
        return new AgentResponse
        {
            Text = (lastAssistantText ?? "") + truncationNotice,
            Usage = totalUsage ?? new TokenUsageDto(),
            FinishReason = FinishReasons.MaxToolIterations,
            Messages = working,
            Citations = null,
            Reasoning = ExtractReasoning(working)
        };
    }

    /// <summary>
    /// 流式执行
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Check.NotNull(messages);

        // 工作副本 - 不修改调用方传入的列表（不变性规则）
        var working = new List<ChatMessage>(messages);

        // 历史压缩（如果 OptionsBuilder 配置了 reducer）
        working = await ReduceHistoryAsync(working, ct);

        // 构建 ChatOptions（输出合并后的完整工具列表）
        // Context injection is owned by ContextInjectionMiddleware (already ran before us).
        var (chatOptions, allTools) = BuildChatOptions();

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

            await foreach (var update in _chatClient.GetStreamingResponseAsync(working, chatOptions, ct))
            {
                // 收集内容：推理块按到达顺序记下，本轮结束时由 PrepareReasoningForHistory 决定哪些进历史
                foreach (var content in update.Contents)
                {
                    if (content is TextReasoningContent reasoning)
                    {
                        accumulatedReasoning.Append(reasoning.Text);
                        assistantContents.Add(reasoning);
                        if (!toolCallSignaled && !string.IsNullOrEmpty(reasoning.Text))
                            yield return new AgentStreamChunk { ReasoningText = reasoning.Text };
                        continue;
                    }

                    assistantContents.Add(content);

                    if (content is FunctionCallContent functionCall)
                    {
                        toolCallContents.Add(functionCall);
                        // Signal tool call once with tool names - any text already streamed is acceptable
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
                    // Fallback: text was flowing but stopped - LLM may be generating function call arguments.
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
                // Always emit text - even in an iteration that also contains tool calls,
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
            // DeepSeek handles content + tool_calls poorly - sending both causes token
            // fracturing (\n\n between every token) in the continuation. The text was
            // already streamed to the client, so omitting it from the history is safe.
            var roundContents = PrepareReasoningForHistory(assistantContents);
            var historyContents = _options.StripTextFromToolCallMessages && toolCallContents.Count > 0
                ? roundContents.Where(c => c is not TextContent).ToList()
                : roundContents;
            var assistantMessage = new ChatMessage(ChatRole.Assistant, [.. historyContents]);
            working.Add(assistantMessage);

            // 检查是否有工具调用
            if (toolCallContents.Count == 0)
            {
                // 终止 chunk 只带本轮这一次调用的用量：之前各轮已各自发出（StreamUsage 的增量口径）
                yield return new AgentStreamChunk
                {
                    Usage = ConvertUsage(usage),
                    FinishReason = finishReason?.ToString(),
                    Citations = null
                };
                yield break;
            }

            // Intermediate iteration with tool call - tool call indicator was already sent above.

            // 本轮调用的用量在执行工具之前单独发出。只在终止 chunk 里报用量，前面各轮与跑满
            // MaxToolIterations 的整段都量不到；放在执行工具之前，是因为 Router / Handoff 在工具
            // 执行完的那一刻就可能中止本执行器，之后的 chunk 不会再被读取。
            var iterationUsage = ConvertUsage(usage);
            if (iterationUsage != null)
            {
                yield return new AgentStreamChunk { Usage = iterationUsage };
            }

            // 执行工具（含详情，用于客户端性能基准）
            var (toolResults, toolDetails) = await ExecuteToolCallsWithDetailsAsync(toolCallContents, allTools, ct);
            working.Add(new ChatMessage(ChatRole.Tool, [.. toolResults]));

            // Emit tool call details for client-side benchmarking
            yield return new AgentStreamChunk { ToolCalls = toolDetails };

            // 注意: RecordToolCall 已在 ExecuteToolCallsCoreAsync 内部调用，此处不重复记录
        }

        // 达到最大迭代次数 - 发送提示文本告知用户（各轮用量已在循环内逐轮发出）
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
    /// 压缩历史 - invokes the configured IHistoryReducer if any.
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
    /// 构建 ChatOptions，同时返回合并后的完整工具列表。
    /// Tool injection from ContextProvider is now handled at the runtime layer
    /// (AgentRuntime.MergeAdditionalTools) before this executor is called.
    /// </summary>
    private (ChatOptions Options, IList<AITool> AllTools) BuildChatOptions()
    {
        var chatOptions = new ChatOptions();

        // System prompt: wrap the static Instructions in an <instructions> XML section.
        // Dynamic context (soul/profile/memory/skills) is injected upstream by ContextInjectionMiddleware.
        var instructions = SystemPromptTemplateBuilder.WrapInstructions(_options.Instructions);
        if (instructions != null)
        {
            chatOptions.Instructions = instructions;
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

        return (chatOptions, tools);
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
    /// 安全规则：
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
            var detail = new ToolCallDetail { Name = toolCall.Name, Input = ToolInput(toolCall), IsSuccess = false, Error = "Tool not found" };
            return (new FunctionResultContent(toolCall.CallId, $"Tool '{toolCall.Name}' not found"), detail);
        }

        // 构建参数摘要（用于追踪，截取前 200 字符避免大参数污染 trace）
        var argSummary = toolCall.Arguments is { Count: > 0 }
            ? string.Join(", ", toolCall.Arguments.Select(kv => $"{kv.Key}={Truncate(kv.Value?.ToString(), 50)}"))
            : null;
        argSummary = Truncate(argSummary, 200);

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

                // ★ 超时要取消工具，而不是丢下它不管：此前只在等待上加了超时（WaitAsync），交给工具的令牌从不取消。
                // 模型被告知「超时」并继续下一轮，原调用却还在跑：审批晚到时照样执行那个破坏性工具，
                // wait_agent 之类的轮询与下一轮的工具、历史落库同时用同一个作用域的 DbContext。
                // 预算到点即取消令牌；有自己上限的等待（审批、子 Agent、wait_agent）经 ToolCallTimeout.Suspend 排除在外。
                using var timeoutCts = new CancellationTokenSource();
                using var budget = ToolCallTimeout.Begin(timeoutCts, TimeSpan.FromSeconds(_options.ToolTimeoutSeconds));

                try
                {
                    if (interruptBehavior == ToolInterruptBehavior.GracefulShutdown)
                    {
                        // GracefulShutdown: 不直接链接外部 CT（链接会立即传播取消），
                        // 而是用独立 CTS + 回调延迟取消，给工具宽限期完成清理。超时同样是一次中断，同样给宽限期。
                        using var graceCts = new CancellationTokenSource();
                        using var registration = ct.Register(() => graceCts.CancelAfter(GracefulShutdownGracePeriod));
                        using var timeoutRegistration = timeoutCts.Token.Register(() => graceCts.CancelAfter(GracefulShutdownGracePeriod));

                        if (ct.IsCancellationRequested)
                        {
                            // 外部已取消 → 启动宽限倒计时
                            graceCts.CancelAfter(GracefulShutdownGracePeriod);
                        }

                        return await aiFunction.InvokeAsync(args, graceCts.Token).AsTask().WaitAsync(graceCts.Token);
                    }

                    // Cancel (默认): 外部取消与超时都直接传播；不理会令牌的工具在到点时被放弃（它已收到取消信号）
                    using var toolCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                    return await aiFunction.InvokeAsync(args, toolCts.Token).AsTask().WaitAsync(toolCts.Token);
                }
                catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"Tool '{toolCall.Name}' timed out after {_options.ToolTimeoutSeconds}s", ex);
                }
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
                Input = ToolInput(toolCall),
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
                Input = ToolInput(toolCall),
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
            var detail = new ToolCallDetail { Name = toolCall.Name, Input = ToolInput(toolCall), IsSuccess = false, Error = ex.Message };
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

    /// <summary>
    /// 工具调用的参数，按模型发来的 JSON 对象原样记在 <see cref="ToolCallDetail.Input"/> 上（与外部 CLI 路径同形）。
    /// 客户端靠它画工具卡片（例如 <c>show_widget</c> 的内联小部件是从参数里的 HTML 画出来的），刷新与翻历史后也一样。
    /// 序列化不了时省略，不让一次记录失败变成工具调用失败。
    /// </summary>
    private static JsonElement? ToolInput(FunctionCallContent toolCall)
    {
        if (toolCall.Arguments is not { Count: > 0 })
        {
            return null;
        }

        try
        {
            return JsonSerializer.SerializeToElement(toolCall.Arguments);
        }
        catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ResolveToolFailure(ToolExecutionContext context)
    {
        return ToolErrorRecoveryMiddleware.GetRecoveredError(context)
            ?? ToolGuardrailMiddleware.GetDenialReason(context)
            ?? RequiresSkillToolMiddleware.GetShortCircuitReason(context)
            ?? context.FailureReason;
    }

    /// <summary>
    /// 流式一轮的助手内容进历史前处理推理块。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 带 <see cref="TextReasoningContent.ProtectedData"/>（签名）的推理是提供商要求原样回传的原生 thinking 块：
    /// Anthropic 在 thinking 开启时要求最后一组工具调用之前的助手消息以带签名的 thinking 块开头，缺了整个请求被拒。
    /// 流式下一个块是一串增量片段加最后一个只带签名的片段，这里把每段合并回一个块、签名取那一段的签名，保持原位置。
    /// </para>
    /// <para>
    /// 没有签名的推理（OpenAI 兼容端点的 reasoning_content、&lt;think&gt; 标签切出的片段）是给界面看的合成块，
    /// 提供商不收回，照旧不进历史；一轮里没有任何签名时整轮推理都不进历史。
    /// </para>
    /// </remarks>
    internal static List<AIContent> PrepareReasoningForHistory(IReadOnlyList<AIContent> contents)
    {
        var result = new List<AIContent>(contents.Count);
        StringBuilder? block = null;
        foreach (var content in contents)
        {
            if (content is TextReasoningContent reasoning)
            {
                block ??= new StringBuilder();
                block.Append(reasoning.Text);
                if (reasoning.ProtectedData != null)
                {
                    // 签名片段是一个块的结尾。它的 RawRepresentation / AdditionalProperties 要跟着走：
                    // redacted_thinking 与 display 被省略的 thinking 块长得一样（空文本 + 不透明数据），
                    // Anthropic 适配器只凭这两样认出前者，丢了就把它当成签名对不上的 thinking 块发回去
                    result.Add(new TextReasoningContent(block.ToString())
                    {
                        ProtectedData = reasoning.ProtectedData,
                        RawRepresentation = reasoning.RawRepresentation,
                        AdditionalProperties = reasoning.AdditionalProperties
                    });
                    block = null;
                }
                continue;
            }

            // 被别的内容打断、始终没等到签名的残段不是原生块，不回传（轮末未闭合的同理）
            block = null;
            result.Add(content);
        }

        return result;
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
    /// 标准化工具结果 - 空结果添加标记，防止 LLM 误以为调用失败。
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
    /// 截取字符串（用于追踪摘要）；截断点落在代理对中间时整个字符一起舍掉。
    /// </summary>
    private static string? Truncate(string? value, int maxLength) => value?.Truncate(maxLength);

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
            // Silently ignore - this is a best-effort optimization
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
            TotalTokens = (int)(usage.TotalTokenCount ?? 0),
            // 缓存命中读 MEAI 的一等字段：OpenAI 与 Anthropic 的适配器都写在这里，且按 MEAI 约定它包含在
            // InputTokenCount 之内（Anthropic 适配器的 InputTokenCount = input + cache_read + cache_creation）。
            // 此前只查附加计数里的几个键名，两家适配器都不用那些键，记下的缓存数恒为 0
            CachedInputTokens = (int)(usage.CachedInputTokenCount ?? 0)
        };

        if (usage.AdditionalCounts is { Count: > 0 })
        {
            // 缓存写入没有一等字段：Anthropic 适配器的键是 CacheCreationInputTokens；蛇形键留给原样透传的兼容端点
            if (usage.AdditionalCounts.TryGetValue("CacheCreationInputTokens", out var cacheCreation)
                || usage.AdditionalCounts.TryGetValue("cache_creation_input_tokens", out cacheCreation))
            {
                dto.CacheCreationTokens = (int)cacheCreation;
            }

            // 只把命中数放进附加计数的兼容端点（一等字段为空时才看）
            if (dto.CachedInputTokens == 0
                && (usage.AdditionalCounts.TryGetValue("cache_read_input_tokens", out var cacheRead)
                    || usage.AdditionalCounts.TryGetValue("prompt_cache_hit_tokens", out cacheRead)))
            {
                dto.CachedInputTokens = (int)cacheRead;
            }
        }

        return dto;
    }

}
