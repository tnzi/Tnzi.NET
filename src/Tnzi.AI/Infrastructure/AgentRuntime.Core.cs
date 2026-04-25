namespace Tnzi.AI.Infrastructure;

/// <summary>
/// AgentRuntime — core executor + helpers
/// </summary>
public partial class AgentRuntime
{
    /// <summary>
    /// Setup result returned by <see cref="SetupContextAndResolveAsync"/>.
    /// Captures the per-run context shared between RunAsync and RunStreamingAsync.
    /// </summary>
    private sealed record RunSetupResult(
        AgentResolution Resolution,
        AgentRun? Run,
        AiMiddlewareContext Context);

    /// <summary>
    /// Shared prelude for both RunAsync and RunStreamingAsync:
    /// resolve thinking model → resolve agent → get/create run → publish started event → build context.
    /// Returns null if Agent resolution fails (caller decides how to surface the error).
    /// </summary>
    private async Task<RunSetupResult?> SetupContextAndResolveAsync(
        AgentRunRequest request,
        bool isStreaming,
        CancellationToken ct)
    {
        var effectiveModel = ResolveThinkingModel(request);

        var resolution = await _agentResolver.ResolveAgentAsync(
            request.AgentId, request.Provider, effectiveModel, request.ToolGroups, ct);

        if (!resolution.IsSuccess)
        {
            return new RunSetupResult(resolution, null, null!);
        }

        AgentRun? run = null;
        if (request.EnableRunTracking)
        {
            run = await _runTracker.GetOrCreateRunAsync(request, resolution, ct);
            _executionContextAccessor.Properties[ContextPropertyKeys.CurrentRunId] = run.Id;
        }

        await _eventPublisher.PublishRunStartedEventAsync(
            request, run, isStreaming, resolution.Provider, resolution.Model, resolution.ExecutionMode);

        var context = new AiMiddlewareContext
        {
            Request = request,
            Agent = resolution,
            Run = run,
            ServiceProvider = _serviceProvider
        };

        return new RunSetupResult(resolution, run, context);
    }

    /// <summary>
    /// Core executor (non-streaming) — innermost pipeline layer, delegates to execution strategy.
    /// </summary>
    private async Task<AgentRunResult> ExecuteCoreAsync(AiMiddlewareContext context, CancellationToken ct)
    {
        var resolution = context.Agent;

        if (resolution.ExecutionMode == AgentExecutionMode.ExternalCli)
        {
            var cliExecutor = _serviceProvider.GetRequiredService<IExternalCliExecutor>();
            return await cliExecutor.ExecuteCliAsync(context, ct);
        }

        var agent = await ApplyModelOverrideAsync(resolution, context, ct);

        var messages = new List<ChatMessage>(context.Messages);
        if (!string.IsNullOrWhiteSpace(context.Request.UserMessage))
        {
            var userMessage = await _agentResolver.BuildChatMessageAsync(
                context.Request.UserMessage, context.Request.ContentParts, ct);
            messages.Add(userMessage);
        }

        agent = MergeAdditionalTools(agent, context);

        var strategy = ExecutionStrategyResolver.Resolve(resolution.ExecutionMode, resolution.AgentConfiguration);
        var strategyContext = new ExecutionStrategyContext
        {
            AgentFactory = _agentFactory,
            AgentRepository = _agentRepository,
            ServiceProvider = _serviceProvider,
            ExecutionContextAccessor = _executionContextAccessor,
            Logger = _logger,
            StartingAgentId = resolution.AgentId
        };

        using (ToolContext.Establish(_serviceProvider, ct))
        {
            var executionResult = await strategy.ExecuteAsync(agent, messages, strategyContext, ct);
            var response = executionResult.Response;
            var actualModel = context.EffectiveModel ?? resolution.Model;
            var actualProvider = context.EffectiveProvider ?? resolution.Provider;

            return new AgentRunResult
            {
                Response = response.Text ?? string.Empty,
                ThreadId = context.Request.ThreadId,
                Usage = executionResult.AggregatedUsage ?? response.Usage,
                Citations = context.Citations.Count > 0 ? context.Citations : null,
                FinishReason = response.FinishReason,
                Model = actualModel,
                Provider = actualProvider,
                HandoffPath = executionResult.HandoffPath,
                FinalAgentName = executionResult.FinalAgentName,
                Reasoning = response.Reasoning
            };
        }
    }

    /// <summary>
    /// Core executor (streaming) — innermost pipeline layer, delegates to execution strategy.
    /// </summary>
    private async IAsyncEnumerable<AgentStreamChunk> ExecuteCoreStreamingAsync(
        AiMiddlewareContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var resolution = context.Agent;

        if (resolution.ExecutionMode == AgentExecutionMode.ExternalCli)
        {
            var cliExecutor = _serviceProvider.GetRequiredService<IExternalCliExecutor>();
            await foreach (var chunk in cliExecutor.ExecuteCliStreamingAsync(context, ct))
            {
                yield return chunk;
            }
            yield break;
        }

        var agent = await ApplyModelOverrideAsync(resolution, context, ct);

        var messages = new List<ChatMessage>(context.Messages);
        if (!string.IsNullOrWhiteSpace(context.Request.UserMessage))
        {
            var userMessage = await _agentResolver.BuildChatMessageAsync(
                context.Request.UserMessage, context.Request.ContentParts, ct);
            messages.Add(userMessage);
        }

        agent = MergeAdditionalTools(agent, context);

        var strategy = ExecutionStrategyResolver.Resolve(resolution.ExecutionMode, resolution.AgentConfiguration);
        var pendingEvents = new ConcurrentQueue<AgentStreamChunk>();
        var strategyContext = new ExecutionStrategyContext
        {
            AgentFactory = _agentFactory,
            AgentRepository = _agentRepository,
            ServiceProvider = _serviceProvider,
            ExecutionContextAccessor = _executionContextAccessor,
            Logger = _logger,
            StartingAgentId = resolution.AgentId,
            EmitEvent = chunk => pendingEvents.Enqueue(chunk)
        };

        using var scope = ToolContext.Establish(_serviceProvider, ct);

        await foreach (var chunk in strategy.ExecuteStreamingAsync(agent, messages, strategyContext, ct).WithCancellation(ct))
        {
            while (pendingEvents.TryDequeue(out var evt))
                yield return evt;

            yield return chunk;
        }

        while (pendingEvents.TryDequeue(out var remaining))
            yield return remaining;
    }

    /// <summary>
    /// Apply EffectiveModel/Provider override set by SkillConstraintMiddleware.
    /// Rebuilds AgentExecutor if Model or Provider changed; returns original otherwise.
    /// </summary>
    private async Task<AgentExecutor> ApplyModelOverrideAsync(AgentResolution resolution, AiMiddlewareContext context, CancellationToken ct)
    {
        var originalAgent = resolution.Agent!;
        var effectiveModel = context.EffectiveModel;
        var effectiveProvider = context.EffectiveProvider;

        if (effectiveModel == null && effectiveProvider == null)
            return originalAgent;

        var modelChanged = effectiveModel != null && !string.Equals(effectiveModel, resolution.Model, StringComparison.OrdinalIgnoreCase);
        var providerChanged = effectiveProvider != null && !string.Equals(effectiveProvider, resolution.Provider, StringComparison.OrdinalIgnoreCase);

        if (!modelChanged && !providerChanged)
            return originalAgent;

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
    /// Merge middleware-injected tools into Agent, deduplicating by name.
    /// </summary>
    private static AgentExecutor MergeAdditionalTools(AgentExecutor agent, AiMiddlewareContext context)
    {
        if (context.AdditionalTools.Count == 0)
            return agent;

        var existingNames = agent.Tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newTools = context.AdditionalTools.Where(t => !existingNames.Contains(t.Name)).ToList();

        return newTools.Count > 0 ? agent.WithAdditionalTools(newTools) : agent;
    }

    /// <summary>
    /// Resolve all registered middlewares from DI, ordered by Order.
    /// List is cached on first access.
    /// </summary>
    private List<IAiMiddleware> ResolveMiddlewares() => _middlewares.Value;

    /// <summary>
    /// Build non-streaming pipeline delegate (scope-cached).
    /// </summary>
    private AiMiddlewareDelegate BuildPipelineDelegate()
    {
        var pipeline = new AiMiddlewarePipeline();
        foreach (var middleware in ResolveMiddlewares())
        {
            pipeline.Use(middleware);
        }
        return pipeline.Build(ExecuteCoreAsync);
    }

    /// <summary>
    /// Build streaming pipeline delegate (scope-cached).
    /// </summary>
    private AiStreamingMiddlewareDelegate BuildStreamingPipelineDelegate()
    {
        var pipeline = new AiMiddlewarePipeline();
        foreach (var middleware in ResolveMiddlewares())
        {
            pipeline.Use(middleware);
        }
        return pipeline.BuildStreaming(ExecuteCoreStreamingAsync);
    }

    /// <summary>
    /// Auto-resolve effective model based on ReasoningEffort.
    /// When ReasoningEffort != None and current model doesn't support reasoning,
    /// look up the provider's "think" model alias.
    /// </summary>
    private string? ResolveThinkingModel(AgentRunRequest request)
    {
        var model = request.Model;

        if (request.ReasoningEffort is null or ReasoningEffort.None) return model;

        if (ModelCapabilities.SupportsReasoning(model) || ModelCapabilities.IsAlwaysOnReasoning(model))
            return model;

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
}
