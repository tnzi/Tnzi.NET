namespace Tnzi.AI.Infrastructure;

/// <summary>
/// AgentRuntime — 核心执行器 + 辅助方法
/// </summary>
public partial class AgentRuntime
{
    /// <summary>
    /// 核心执行器（非流式）— 管道最内层，委托给执行策略
    /// </summary>
    private async Task<AgentRunResult> ExecuteCoreAsync(AiMiddlewareContext context, CancellationToken ct)
    {
        var resolution = context.Agent;

        // ExternalCli 模式：不需要 AgentExecutor，委托给 CLI 策略
        if (resolution.ExecutionMode == AgentExecutionMode.ExternalCli)
        {
            var cliExecutor = _serviceProvider.GetService<IExternalCliExecutor>()
                ?? throw new BusinessException(
                    "ExternalCli mode requires Tnzi.AI.Cli module. Add [DependsOn(typeof(AICliModule))] to your startup module.");
            return await cliExecutor.ExecuteCliAsync(context, ct);
        }

        // 应用 EffectiveModel/Provider 覆盖（由 SkillConstraintMiddleware 设置）
        var agent = await ApplyModelOverrideAsync(resolution, context, ct);

        // 构建消息列表（包含中间件注入的消息）
        var messages = new List<ChatMessage>(context.Messages);
        if (!string.IsNullOrWhiteSpace(context.Request.UserMessage))
        {
            var userMessage = await _agentResolver.BuildChatMessageAsync(
                context.Request.UserMessage, context.Request.ContentParts, ct);
            messages.Add(userMessage);
        }

        // 合并中间件注入的工具（Skill 三件套等），按名称去重
        agent = MergeAdditionalTools(agent, context);

        // 解析并执行策略
        var strategy = ExecutionStrategyResolver.Resolve(resolution.ExecutionMode, resolution.AgentConfiguration);
        var strategyContext = new ExecutionStrategyContext
        {
            AgentFactory = _agentFactory,
            AgentRepository = _agentRepository,
            ServiceProvider = _serviceProvider,
            Logger = _logger,
            StartingAgentId = resolution.AgentId
        };

        using (ToolContext.Establish(_serviceProvider, ct))
        {
            var executionResult = await strategy.ExecuteAsync(agent, messages, strategyContext, ct);
            var response = executionResult.Response;

            return new AgentRunResult
            {
                Response = response.Text ?? string.Empty,
                ThreadId = context.Request.ThreadId,
                Usage = executionResult.AggregatedUsage ?? response.Usage,
                Citations = context.Citations.Count > 0 ? context.Citations : null,
                FinishReason = response.FinishReason,
                HandoffPath = executionResult.HandoffPath,
                FinalAgentName = executionResult.FinalAgentName,
                Reasoning = response.Reasoning
            };
        }
    }

    /// <summary>
    /// 核心执行器（流式）— 管道最内层，委托给执行策略
    /// </summary>
    private async IAsyncEnumerable<AgentStreamChunk> ExecuteCoreStreamingAsync(
        AiMiddlewareContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var resolution = context.Agent;

        // ExternalCli 模式：不需要 AgentExecutor，委托给 CLI 策略
        if (resolution.ExecutionMode == AgentExecutionMode.ExternalCli)
        {
            var cliExecutor = _serviceProvider.GetService<IExternalCliExecutor>()
                ?? throw new BusinessException(
                    "ExternalCli mode requires Tnzi.AI.Cli module. Add [DependsOn(typeof(AICliModule))] to your startup module.");
            await foreach (var chunk in cliExecutor.ExecuteCliStreamingAsync(context, ct))
            {
                yield return chunk;
            }
            yield break;
        }

        // 应用 EffectiveModel/Provider 覆盖（由 SkillConstraintMiddleware 设置）
        var agent = await ApplyModelOverrideAsync(resolution, context, ct);

        // 构建消息列表
        var messages = new List<ChatMessage>(context.Messages);
        if (!string.IsNullOrWhiteSpace(context.Request.UserMessage))
        {
            var userMessage = await _agentResolver.BuildChatMessageAsync(
                context.Request.UserMessage, context.Request.ContentParts, ct);
            messages.Add(userMessage);
        }

        // 合并中间件注入的工具（Skill 三件套等），按名称去重
        agent = MergeAdditionalTools(agent, context);

        var strategy = ExecutionStrategyResolver.Resolve(resolution.ExecutionMode, resolution.AgentConfiguration);
        var strategyContext = new ExecutionStrategyContext
        {
            AgentFactory = _agentFactory,
            AgentRepository = _agentRepository,
            ServiceProvider = _serviceProvider,
            Logger = _logger,
            StartingAgentId = resolution.AgentId
        };

        using var scope = ToolContext.Establish(_serviceProvider, ct);

        await foreach (var chunk in strategy.ExecuteStreamingAsync(agent, messages, strategyContext, ct).WithCancellation(ct))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 应用 SkillConstraintMiddleware 设置的 EffectiveModel/Provider 覆盖。
    /// 若 Model 或 Provider 发生变化且保存了创建参数，则重建 AgentExecutor；否则返回原始 Agent。
    /// </summary>
    private async Task<AgentExecutor> ApplyModelOverrideAsync(AgentResolution resolution, AiMiddlewareContext context, CancellationToken ct)
    {
        var originalAgent = resolution.Agent!;
        var effectiveModel = context.EffectiveModel;
        var effectiveProvider = context.EffectiveProvider;

        // 没有覆盖 → 直接使用原始 Agent
        if (effectiveModel == null && effectiveProvider == null)
            return originalAgent;

        var modelChanged = effectiveModel != null && !string.Equals(effectiveModel, resolution.Model, StringComparison.OrdinalIgnoreCase);
        var providerChanged = effectiveProvider != null && !string.Equals(effectiveProvider, resolution.Provider, StringComparison.OrdinalIgnoreCase);

        if (!modelChanged && !providerChanged)
            return originalAgent;

        // 没有原始创建参数（无 AgentId 场景） → 无法重建，记录警告后继续
        if (resolution.CreationParameters == null)
        {
            _logger.LogWarning(
                "SkillConstraintMiddleware requested model/provider override (Model={Model}, Provider={Provider}) " +
                "but AgentResolution has no CreationParameters. Override skipped.",
                effectiveModel, effectiveProvider);
            return originalAgent;
        }

        var p = resolution.CreationParameters;
        var newProvider = effectiveProvider ?? resolution.Provider;
        var newModel = effectiveModel ?? resolution.Model;

        _logger.LogInformation(
            "Skill constraint override: rebuilding AgentExecutor with Provider={Provider}, Model={Model}",
            newProvider, newModel);

        return await _agentFactory.CreateAgentAsync(
            newProvider, newModel, p.Instructions, p.Name, p.ToolGroups,
            p.Temperature, p.MaxTokens, options: null, userPermissions: p.UserPermissions,
            agentId: resolution.AgentId, ct: ct);
    }

    /// <summary>
    /// 合并中间件注入的工具到 Agent（按名称去重，防止 Skill 工具出现两次）
    /// </summary>
    private static AgentExecutor MergeAdditionalTools(AgentExecutor agent, AiMiddlewareContext context)
    {
        if (context.AdditionalTools.Count == 0)
            return agent;

        var existingNames = agent.Tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newTools = context.AdditionalTools.Where(t => !existingNames.Contains(t.Name)).ToList();

        return newTools.Count > 0 ? agent.WithAdditionalTools(newTools) : agent;
    }

    /// <summary>创建 Run 记录</summary>
    private async Task<AgentRun> CreateRunAsync(AgentRunRequest request, AgentResolution resolution, CancellationToken ct)
    {
        var run = new AgentRun
        {
            AgentId = request.AgentId ?? resolution.AgentId,
            ThreadId = request.ThreadId,
            WorkflowDefinitionId = request.WorkflowId,
            Status = AgentRunStatus.Running,
            ExecutionMode = resolution.ExecutionMode,
            InputSummary = Truncate(request.UserMessage, 500)
        };

        return await _runStore.CreateAsync(run, ct);
    }

    /// <summary>记录 Trace 条目</summary>
    private async Task RecordTraceAsync(Guid runId, Guid? nodeId, string eventType, object? eventData, long durationMs, CancellationToken ct)
    {
        try
        {
            var trace = new AgentRunTrace
            {
                RunId = runId,
                NodeId = nodeId,
                EventType = eventType,
                EventData = eventData?.ToJsonString(camelCase: true),
                DurationMs = durationMs
            };
            await _traceStore.AddAsync(trace, ct);
        }
        catch (Exception ex)
        {
            // Trace 记录失败不影响主流程
            _logger.LogWarning(ex, "Failed to record trace for Run {RunId}", runId);
        }
    }

    /// <summary>
    /// 从 DI 解析所有已注册的中间件，按 Order 排序。
    /// 中间件列表在首次访问时缓存，避免每次调用重复解析和排序。
    /// </summary>
    private List<IAiMiddleware> ResolveMiddlewares() => _middlewares.Value;

    /// <summary>
    /// 根据 ReasoningEffort 自动解析有效模型。
    /// 当 ReasoningEffort != None 且当前模型不支持推理时，查找 Provider 的 "think" 模型别名。
    /// </summary>
    private string? ResolveThinkingModel(AgentRunRequest request)
    {
        var model = request.Model;

        // 没有指定推理需求 → 使用原始模型
        if (request.ReasoningEffort is null or ReasoningEffort.None) return model;

        // 当前模型已支持推理 → 不需要切换
        if (ModelCapabilities.SupportsReasoning(model) || ModelCapabilities.IsAlwaysOnReasoning(model))
            return model;

        // 查找 Provider 配置中的 "think" 模型别名
        var providerName = request.Provider;
        var options = _aiOptions.CurrentValue;
        providerName ??= options.DefaultProvider;

        if (!options.Providers.TryGetValue(providerName, out var providerOptions))
            return model;

        if (providerOptions.Models?.TryGetValue("think", out var thinkModel) == true)
        {
            _logger.LogDebug(
                "Auto-switching to think model '{ThinkModel}' for provider '{Provider}' (ReasoningEffort={Effort})",
                thinkModel, providerName, request.ReasoningEffort);
            return thinkModel;
        }

        return model;
    }

    /// <summary>发布运行完成事件（静默失败，不影响主流程）</summary>
    private async Task PublishRunCompletedEventAsync(AgentRunRequest request, AgentRunResult result, AgentRun? run, long durationMs, bool isStreaming)
    {
        try
        {
            if (_eventBus == null) return;

            await _eventBus.PublishAsync(new AgentRunCompletedEvent
            {
                RunId = run?.Id,
                ThreadId = request.ThreadId,
                AgentId = request.AgentId,
                UserId = request.UserId,
                Provider = request.Provider,
                Model = request.Model,
                TotalTokens = (result.Usage?.InputTokens ?? 0) + (result.Usage?.OutputTokens ?? 0),
                DurationMs = durationMs,
                Status = (run?.Status ?? AgentRunStatus.Completed).ToString(),
                FinishReason = result.FinishReason,
                IsStreaming = isStreaming
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AgentRunCompletedEvent");
        }
    }

    /// <summary>新线程首轮对话：应用 fallback 标题 + 发布标题生成事件（静默失败）</summary>
    /// <remarks>
    /// 使用独立 scope 而非请求 scope，因为 streaming 场景下此方法在 finally 块中执行，
    /// 此时请求 scope 可能已被 ASP.NET Core 释放。
    /// </remarks>
    private async Task HandleNewThreadTitleAsync(AgentRunRequest request, AgentRunResult result)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            // Fallback 标题：用户首条消息截取
            var threadOptions = sp.GetService<IOptionsMonitor<ThreadOptions>>();
            var maxLength = threadOptions?.CurrentValue.TitleMaxLength ?? 50;
            var fallbackTitle = AgentThreadService.GenerateFallbackTitle(request.UserMessage, maxLength);
            if (fallbackTitle != null)
            {
                var threadService = sp.GetService<IAgentThreadService>();
                if (threadService != null)
                {
                    await threadService.UpdateTitleAsync(request.ThreadId!.Value, fallbackTitle);
                }
            }

            // 发布事件（触发 AI 标题生成，如果 AutoGenerateTitle 启用）
            // EventBus 是 Singleton，但 handler 在 EventBus 内部创建独立 scope 运行
            var eventBus = sp.GetService<IEventBus>();
            if (eventBus != null && request.ThreadId != null && !string.IsNullOrWhiteSpace(request.UserMessage))
            {
                await eventBus.PublishAsync(new ThreadFirstReplyCompletedEvent
                {
                    ThreadId = request.ThreadId.Value,
                    UserMessage = request.UserMessage,
                    AssistantReply = Truncate(result.Response, 500)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update thread title/publish event for {ThreadId}", request.ThreadId);
        }
    }

    /// <summary>截断字符串</summary>
    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
