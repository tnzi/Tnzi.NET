namespace Tnzi.AI.Cli.Engine;

/// <summary>
/// ExternalCli 执行策略 — 桥接 AiMiddlewareContext 与 CLI 子进程。
/// 实现 IExternalCliExecutor 以便 AgentRuntime 通过 DI 调用。
/// </summary>
public class ExternalCliExecutionStrategy : IExternalCliExecutor
{
    private readonly IEnumerable<ICliAgentProvider> _providers;
    private readonly CliProcessManager _processManager;
    private readonly WorkingDirectoryValidator _workingDirectoryValidator;
    private readonly CliOptions _options;
    private readonly ILogger<ExternalCliExecutionStrategy> _logger;

    public ExternalCliExecutionStrategy(
        IEnumerable<ICliAgentProvider> providers,
        CliProcessManager processManager,
        WorkingDirectoryValidator workingDirectoryValidator,
        IOptions<CliOptions> options,
        ILogger<ExternalCliExecutionStrategy> logger)
    {
        _providers = Check.NotNull(providers);
        _processManager = Check.NotNull(processManager);
        _workingDirectoryValidator = Check.NotNull(workingDirectoryValidator);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<AgentRunResult> ExecuteCliAsync(AiMiddlewareContext context, CancellationToken ct)
    {
        Check.NotNull(context);

        var (provider, providerOptions, request) = PrepareExecution(context);

        var textBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        string? sessionId = null;

        await foreach (var evt in _processManager.ExecuteAsync(
            provider, provider.BuildProcess(request, providerOptions),
            request.Prompt, request.TimeoutSeconds, ct))
        {
            // 捕获会话 ID
            if (evt.EventType == CliEventType.Status && evt.SessionId != null)
            {
                sessionId = evt.SessionId;
            }

            switch (evt.EventType)
            {
                case CliEventType.Text:
                    textBuilder.Append(evt.Content);
                    break;
                case CliEventType.Thinking:
                    reasoningBuilder.Append(evt.Content);
                    break;
                case CliEventType.Error when evt.IsError:
                    _logger.LogWarning("CLI error during execution: {Error}", evt.Content);
                    break;
            }
        }

        // 保存会话 ID 到上下文
        if (sessionId != null)
        {
            context.Properties[CliConstants.CliSessionIdKey] = sessionId;
        }

        if (provider.SupportsSession)
        {
            context.Properties[CliConstants.SupportsSessionKey] = true;
        }

        return new AgentRunResult
        {
            Response = textBuilder.ToString(),
            ThreadId = context.Request.ThreadId,
            RunId = context.Run?.Id,
            FinishReason = "stop",
            Status = AgentRunStatus.Completed,
            Reasoning = reasoningBuilder.Length > 0 ? reasoningBuilder.ToString() : null
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamChunk> ExecuteCliStreamingAsync(
        AiMiddlewareContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        Check.NotNull(context);

        var (provider, providerOptions, request) = PrepareExecution(context);

        await foreach (var evt in _processManager.ExecuteAsync(
            provider, provider.BuildProcess(request, providerOptions),
            request.Prompt, request.TimeoutSeconds, ct))
        {
            // 捕获会话 ID
            if (evt.EventType == CliEventType.Status && evt.SessionId != null)
            {
                context.Properties[CliConstants.CliSessionIdKey] = evt.SessionId;
            }

            var chunk = provider.MapToStreamChunk(evt);
            if (chunk != null)
            {
                yield return chunk;
            }
        }

        if (provider.SupportsSession)
        {
            context.Properties[CliConstants.SupportsSessionKey] = true;
        }
    }

    /// <summary>
    /// 解析工作目录优先级：请求 Metadata > Agent 配置 > Provider 默认
    /// </summary>
    public static string ResolveWorkingDirectory(
        Dictionary<string, object>? metadata,
        ExternalCliExecutionConfigDto? config,
        CliProviderOptions providerOptions)
    {
        // 1. 请求级覆盖
        if (metadata != null
            && metadata.TryGetValue(CliConstants.WorkingDirectoryKey, out var metaDir)
            && metaDir is string metaDirStr
            && !string.IsNullOrWhiteSpace(metaDirStr))
        {
            return metaDirStr;
        }

        // 2. Agent 配置
        if (!string.IsNullOrWhiteSpace(config?.WorkingDirectory))
        {
            return config.WorkingDirectory;
        }

        // 3. Provider 默认
        if (!string.IsNullOrWhiteSpace(providerOptions.DefaultWorkingDirectory))
        {
            return providerOptions.DefaultWorkingDirectory;
        }

        throw new BusinessException(
            "Working directory is required for ExternalCli mode. " +
            "Specify via request metadata, agent configuration, or provider default.");
    }

    /// <summary>
    /// 解析 CLI Provider 名称优先级：Agent 配置 > 全局默认
    /// </summary>
    public static string ResolveCliProviderName(
        ExternalCliExecutionConfigDto? config,
        CliOptions globalOptions)
    {
        if (!string.IsNullOrWhiteSpace(config?.Provider))
        {
            return config.Provider;
        }

        if (!string.IsNullOrWhiteSpace(globalOptions.DefaultProvider))
        {
            return globalOptions.DefaultProvider;
        }

        throw new BusinessException(
            "CLI provider name is required. Specify in agent ExecutionConfig.ExternalCli.Provider or AI:Cli:DefaultProvider.");
    }

    /// <summary>
    /// 准备执行所需的所有参数
    /// </summary>
    private (ICliAgentProvider Provider, CliProviderOptions ProviderOptions, CliAgentRequest Request) PrepareExecution(
        AiMiddlewareContext context)
    {
        var resolution = context.Agent;
        var executionConfig = AgentExecutionConfigDto.Deserialize(resolution.AgentConfiguration);
        var cliConfig = executionConfig?.ExternalCli;

        // 解析 Provider 名称并查找实例
        var providerName = ResolveCliProviderName(cliConfig, _options);
        var provider = _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new BusinessException(
                $"CLI provider '{providerName}' not found. Available: {string.Join(", ", _providers.Select(p => p.ProviderName))}");

        // 获取 Provider 配置
        if (!_options.Providers.TryGetValue(providerName, out var providerOptions))
        {
            throw new BusinessException(
                $"CLI provider '{providerName}' has no configuration in AI:Cli:Providers.");
        }

        // 解析并验证工作目录
        var workingDirectory = ResolveWorkingDirectory(
            context.Request.Metadata, cliConfig, providerOptions);
        _workingDirectoryValidator.Validate(workingDirectory);

        // 构建请求
        var prompt = context.Request.UserMessage
            ?? throw new BusinessException("UserMessage is required for ExternalCli mode.");

        var request = new CliAgentRequest
        {
            Prompt = prompt,
            WorkingDirectory = workingDirectory,
            Model = resolution.Model,
            AllowedTools = providerOptions.AllowedTools is { Count: > 0 } ? providerOptions.AllowedTools : null,
            EnvironmentVariables = providerOptions.EnvironmentVariables is { Count: > 0 } ? providerOptions.EnvironmentVariables : null,
            TimeoutSeconds = _options.TimeoutSeconds
        };

        _logger.LogInformation(
            "Executing CLI agent: Provider={Provider}, WorkingDirectory={WorkingDirectory}",
            providerName, workingDirectory);

        return (provider, providerOptions, request);
    }
}
