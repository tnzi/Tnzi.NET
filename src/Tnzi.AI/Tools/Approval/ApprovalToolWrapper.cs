namespace Tnzi.AI.Tools.Approval;

/// <summary>
/// 包装 AITool，在执行前根据配置检查审批
/// </summary>
/// <remarks>
/// 当 ToolApproval.Enabled 且工具命中审批策略时，先调用 IToolApprovalHandler.RequestApprovalAsync，
/// 批准后执行原工具，否则返回拒绝结果。
/// </remarks>
public sealed class ApprovalToolWrapper : DelegatingAIFunction
{
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly ToolApprovalOptions _options;
    private readonly IToolPermissionEvaluator? _permissionEvaluator;
    private readonly IShellCommandAnalyzer? _shellCommandAnalyzer;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;
    private readonly ILogger<ApprovalToolWrapper>? _logger;
    private readonly string? _toolGroup;
    private readonly IEventBus? _eventBus;

    /// <summary>
    /// 初始化包装器
    /// </summary>
    /// <param name="innerFunction">被包装的原始工具</param>
    /// <param name="approvalHandler">审批处理器；为空时不发起审批</param>
    /// <param name="options">审批配置</param>
    /// <param name="permissionEvaluator">可选工具权限规则评估器（allow/deny/ask）</param>
    /// <param name="shellCommandAnalyzer">可选 shell 命令分析器（识别管道/破坏性命令）</param>
    /// <param name="executionContextAccessor">可选执行上下文访问器（取当前 Agent/会话标识）</param>
    /// <param name="logger">可选日志</param>
    /// <param name="toolGroup">工具所属组名（用于 AlwaysRequireApprovalGroups 判断及审批请求）</param>
    /// <param name="eventBus">可选事件总线，用于发布审批事件</param>
    public ApprovalToolWrapper(
        AIFunction innerFunction,
        IToolApprovalHandler? approvalHandler,
        ToolApprovalOptions options,
        IToolPermissionEvaluator? permissionEvaluator = null,
        IShellCommandAnalyzer? shellCommandAnalyzer = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null,
        ILogger<ApprovalToolWrapper>? logger = null,
        string? toolGroup = null,
        IEventBus? eventBus = null)
        : base(innerFunction)
    {
        _approvalHandler = approvalHandler;
        _options = Check.NotNull(options);
        _permissionEvaluator = permissionEvaluator;
        _shellCommandAnalyzer = shellCommandAnalyzer;
        _executionContextAccessor = executionContextAccessor;
        _logger = logger;
        _toolGroup = toolGroup;
        _eventBus = eventBus;
    }

    /// <summary>
    /// 包装工具列表：对需要审批的工具用 ApprovalToolWrapper 包装
    /// </summary>
    /// <param name="tools">原始工具列表</param>
    /// <param name="approvalHandler">审批处理器</param>
    /// <param name="options">审批配置</param>
    /// <param name="permissionEvaluator">可选工具权限规则评估器（allow/deny/ask）</param>
    /// <param name="shellCommandAnalyzer">可选 shell 命令分析器（识别管道/破坏性命令）</param>
    /// <param name="executionContextAccessor">可选执行上下文访问器（取当前 Agent/会话标识）</param>
    /// <param name="logger">可选日志</param>
    /// <param name="toolNameToGroup">工具名到组名的映射（用于 AlwaysRequireApprovalGroups 及审批请求 ToolGroup）</param>
    /// <param name="eventBus">可选事件总线，用于发布审批事件</param>
    /// <returns>包装后的工具列表（未启用审批或非 AIFunction 的保持原样）</returns>
    public static IList<AITool> Wrap(
        IList<AITool> tools,
        IToolApprovalHandler? approvalHandler,
        ToolApprovalOptions options,
        IToolPermissionEvaluator? permissionEvaluator = null,
        IShellCommandAnalyzer? shellCommandAnalyzer = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null,
        ILogger<ApprovalToolWrapper>? logger = null,
        IReadOnlyDictionary<string, string>? toolNameToGroup = null,
        IEventBus? eventBus = null)
    {
        if (tools == null || tools.Count == 0)
        {
            return tools ?? (IList<AITool>)new List<AITool>();
        }

        if (!options.Enabled && permissionEvaluator == null)
        {
            return tools;
        }

        var result = new List<AITool>(tools.Count);
        foreach (var tool in tools)
        {
            if (tool is AIFunction aiFunction)
            {
                string? group = null;
                if (toolNameToGroup != null && toolNameToGroup.TryGetValue(aiFunction.Name, out var g))
                {
                    group = g;
                }

                if (permissionEvaluator != null || RequiresApproval(aiFunction.Name, group, options))
                {
                    result.Add(new ApprovalToolWrapper(aiFunction, approvalHandler, options, permissionEvaluator, shellCommandAnalyzer, executionContextAccessor, logger, group, eventBus));
                }
                else
                {
                    result.Add(tool);
                }
            }
            else
            {
                result.Add(tool);
            }
        }

        return result;
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var decision = EvaluateDecision(arguments);

        if (decision.Behavior == PermissionBehavior.Deny)
        {
            _logger?.LogInformation("Tool call rejected by permission rule: {ToolName}, Reason: {Reason}", decision.ToolName, decision.Reason);
            await PublishApprovalEventAsync(decision, "Deny");
            return $"Tool call rejected: {decision.Reason ?? "Not approved"}";
        }

        if (decision.Behavior == PermissionBehavior.Ask)
        {
            if (_approvalHandler == null)
            {
                _logger?.LogWarning("Tool call requires approval but no approval handler is configured: {ToolName}", decision.ToolName);
                return "Tool call rejected: approval handler is not configured.";
            }

            var request = BuildRequest(InnerFunction, arguments, _options);
            _logger?.LogDebug("Requesting approval for tool: {ToolName}", request.ToolName);
            await PublishApprovalEventAsync(decision, "Ask");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            ToolApprovalResult result;
            try
            {
                result = await _approvalHandler.RequestApprovalAsync(request, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 仅当由审批超时（CancelAfter）触发时返回超时文案；外部取消则继续抛出
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                _logger?.LogInformation("Tool approval timed out: {ToolName}", request.ToolName);
                return "Tool call timed out: approval request exceeded the configured timeout.";
            }
            catch (Exception ex) when (ex.InnerException is OperationCanceledException or TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                _logger?.LogInformation("Tool approval timed out: {ToolName}", request.ToolName);
                return "Tool call timed out: approval request exceeded the configured timeout.";
            }

            if (!result.Approved)
            {
                _logger?.LogInformation("Tool call rejected: {ToolName}, Reason: {Reason}", request.ToolName, result.RejectionReason);
                return $"Tool call rejected: {result.RejectionReason ?? "Not approved"}";
            }

            if (result.ModifiedArguments is { Count: > 0 })
            {
                arguments = new AIFunctionArguments(result.ModifiedArguments);
            }
        }

        // 执行工具并发布执行事件
        var toolSw = Stopwatch.StartNew();
        object? toolResult;
        string? errorMessage = null;
        var success = true;
        try
        {
            toolResult = await base.InvokeCoreAsync(arguments, cancellationToken);
        }
        catch (Exception ex)
        {
            success = false;
            errorMessage = ex.Message;
            toolSw.Stop();
            await PublishToolCallEventAsync(toolSw.ElapsedMilliseconds, success, errorMessage);
            throw;
        }

        toolSw.Stop();
        await PublishToolCallEventAsync(toolSw.ElapsedMilliseconds, success, errorMessage);
        return toolResult;
    }

    private async Task PublishToolCallEventAsync(long durationMs, bool success, string? errorMessage)
    {
        try
        {
            if (_eventBus == null) return;

            var runId = _executionContextAccessor?.Properties.TryGetValue(ContextPropertyKeys.CurrentRunId, out var val) == true
                ? val as Guid? : null;

            await _eventBus.PublishAsync(new ToolCallExecutedEvent
            {
                RunId = runId,
                ThreadId = _executionContextAccessor?.CurrentRequest?.ThreadId,
                ToolName = InnerFunction.Name,
                ToolGroup = _toolGroup,
                DurationMs = durationMs,
                Success = success,
                ErrorMessage = errorMessage
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to publish ToolCallExecutedEvent for tool {ToolName}", InnerFunction.Name);
        }
    }

    private async Task PublishApprovalEventAsync(ToolPermissionDecision decision, string behavior)
    {
        try
        {
            if (_eventBus == null) return;

            var runId = _executionContextAccessor?.Properties.TryGetValue(ContextPropertyKeys.CurrentRunId, out var val) == true
                ? val as Guid? : null;

            await _eventBus.PublishAsync(new ApprovalRequestedEvent
            {
                RunId = runId,
                ToolName = decision.ToolName,
                Decision = behavior,
                Reason = decision.Reason,
                UserId = _executionContextAccessor?.CurrentRequest?.UserId
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to publish ApprovalRequestedEvent for tool {ToolName}", decision.ToolName);
        }
    }

    private ToolPermissionDecision EvaluateDecision(AIFunctionArguments arguments)
    {
        if (_permissionEvaluator != null)
        {
            var context = BuildPermissionContext(InnerFunction, arguments, _toolGroup, _shellCommandAnalyzer);
            EnrichPermissionContextFromRuntime(context, InnerFunction, _executionContextAccessor);
            var compatibilityRules = ToolApprovalOptionsRuleAdapter.ToRules(_options);

            // Merge parent session rules when running inside a sub-agent
            var parentRules = GetParentSessionRules(_executionContextAccessor);
            if (parentRules != null)
            {
                compatibilityRules = compatibilityRules.Count > 0
                    ? [.. compatibilityRules, .. parentRules]
                    : parentRules;
            }

            return _permissionEvaluator.Evaluate(context, compatibilityRules);
        }

        return RequiresApproval(InnerFunction.Name, _toolGroup, _options)
            ? new ToolPermissionDecision(InnerFunction.Name, PermissionBehavior.Ask, "Approval required by configuration")
            : new ToolPermissionDecision(InnerFunction.Name, PermissionBehavior.Allow);
    }

    private static bool RequiresApproval(string toolName, string? toolGroup, ToolApprovalOptions options)
    {
        if (!options.Enabled)
        {
            return false;
        }

        if (options.Mode == ToolApprovalMode.NeverRequire)
        {
            return false;
        }

        if (options.NeverRequireApproval.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (options.Mode == ToolApprovalMode.AlwaysRequire)
        {
            return true;
        }

        if (options.Mode == ToolApprovalMode.Specific)
        {
            if (options.AlwaysRequireApproval.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (toolGroup != null && options.AlwaysRequireApprovalGroups.Contains(toolGroup, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ToolPermissionContext BuildPermissionContext(
        AIFunction function,
        AIFunctionArguments arguments,
        string? toolGroup,
        IShellCommandAnalyzer? shellCommandAnalyzer)
    {
        var argsDict = new Dictionary<string, object?>();
        foreach (var kv in arguments)
        {
            argsDict[kv.Key] = kv.Value;
        }

        var context = new ToolPermissionContext
        {
            ToolName = function.Name,
            ToolGroup = toolGroup,
            Arguments = argsDict
        };

        context.WorkingDirectory = GetStringArgument(argsDict, "working_directory");
        context.CandidatePaths = ExtractCandidatePaths(argsDict, context.WorkingDirectory);

        var shellCommand = GetStringArgument(argsDict, "command")
            ?? GetStringArgument(argsDict, "script")
            ?? GetStringArgument(argsDict, "cmd");

        if (!string.IsNullOrWhiteSpace(shellCommand))
        {
            context.ShellCommand = shellCommand;
            if (shellCommandAnalyzer != null)
            {
                var analysis = shellCommandAnalyzer.Analyze(shellCommand);
                context.ShellSegments = analysis.Segments.Select(x => x.CommandText).ToList();
                context.IsDestructive = analysis.IsDestructiveCandidate;
            }
        }

        return context;
    }

    private static void EnrichPermissionContextFromRuntime(
        ToolPermissionContext context,
        AIFunction function,
        IAgentExecutionContextAccessor? executionContextAccessor)
    {
        context.ServerName = GetAdditionalPropertyString(function, "mcp.server");

        if (string.IsNullOrWhiteSpace(context.ServerName)
            && context.ToolGroup != null
            && context.ToolGroup.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
        {
            context.ServerName = context.ToolGroup["mcp:".Length..];
        }

        if (executionContextAccessor?.Properties.TryGetValue(ContextPropertyKeys.IsSubAgent, out var isSubAgent) == true)
        {
            context.IsSubAgent = TryConvertToBool(isSubAgent);
        }

        if (executionContextAccessor?.Properties.TryGetValue(ContextPropertyKeys.SubAgentName, out var subAgentName) == true)
        {
            context.SubAgentName = ConvertToString(subAgentName);
        }

        var currentRequest = executionContextAccessor?.CurrentRequest;
        context.WorkflowId = currentRequest?.WorkflowId;
        context.IsWorkflowRun = currentRequest?.WorkflowId.HasValue == true;
        context.WorkflowExecutionId = GetContextPropertyString(executionContextAccessor, ContextPropertyKeys.WorkflowExecutionId);
        context.WorkflowNodeName = GetContextPropertyString(executionContextAccessor, ContextPropertyKeys.WorkflowNodeName);

        if (currentRequest?.Metadata != null)
        {
            context.WorkflowExecutionId ??=
                GetMetadataString(currentRequest.Metadata, "workflow_execution_id")
                ?? GetMetadataString(currentRequest.Metadata, "workflowExecutionId");
            context.WorkflowNodeName ??=
                GetMetadataString(currentRequest.Metadata, "node_name")
                ?? GetMetadataString(currentRequest.Metadata, "nodeName");
        }

        if (currentRequest?.Metadata?.TryGetValue("working_directory", out var workingDirectory) == true
            && string.IsNullOrWhiteSpace(context.WorkingDirectory))
        {
            context.WorkingDirectory = ConvertToString(workingDirectory);
        }

        if (context.CandidatePaths.Count > 0)
        {
            context.CandidatePaths = NormalizeCandidatePaths(context.CandidatePaths, context.WorkingDirectory);
        }

        if (context.CandidatePaths.Count == 0 && !string.IsNullOrWhiteSpace(context.WorkingDirectory))
        {
            context.CandidatePaths = [context.WorkingDirectory];
        }
    }

    private static IReadOnlyList<string> ExtractCandidatePaths(
        IReadOnlyDictionary<string, object?> arguments,
        string? workingDirectory)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var normalized = ResolveCandidatePath(value, workingDirectory);
            if (seen.Add(normalized))
            {
                paths.Add(normalized);
            }
        }

        AddPath(workingDirectory);

        foreach (var (key, value) in arguments)
        {
            if (!LooksLikePathArgument(key))
            {
                continue;
            }

            switch (value)
            {
                case string text:
                    AddPath(text);
                    break;
                case JsonElement json when json.ValueKind == JsonValueKind.String:
                    AddPath(json.GetString());
                    break;
                case IEnumerable<string> texts:
                    foreach (var text in texts)
                    {
                        AddPath(text);
                    }
                    break;
                case JsonElement json when json.ValueKind == JsonValueKind.Array:
                    foreach (var item in json.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            AddPath(item.GetString());
                        }
                    }
                    break;
            }
        }

        return paths;
    }

    private static IReadOnlyList<string> NormalizeCandidatePaths(
        IReadOnlyList<string> candidatePaths,
        string? workingDirectory)
    {
        if (candidatePaths.Count == 0)
        {
            return candidatePaths;
        }

        var normalized = new List<string>(candidatePaths.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidatePath in candidatePaths)
        {
            var resolved = ResolveCandidatePath(candidatePath, workingDirectory);
            if (seen.Add(resolved))
            {
                normalized.Add(resolved);
            }
        }

        return normalized;
    }

    private static string ResolveCandidatePath(string value, string? workingDirectory)
    {
        var trimmed = value.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return trimmed;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(workingDirectory) && !Path.IsPathRooted(trimmed))
            {
                return Path.GetFullPath(trimmed, workingDirectory);
            }

            if (Path.IsPathRooted(trimmed))
            {
                return Path.GetFullPath(trimmed);
            }
        }
        catch
        {
            // Best-effort normalization only. If resolution fails, keep the original value
            // so the permission evaluator still sees the caller-provided path.
        }

        return trimmed;
    }

    private static bool LooksLikePathArgument(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return key.Contains("path", StringComparison.OrdinalIgnoreCase)
            || key.Contains("file", StringComparison.OrdinalIgnoreCase)
            || key.Contains("directory", StringComparison.OrdinalIgnoreCase)
            || key.Contains("folder", StringComparison.OrdinalIgnoreCase)
            || key.Contains("cwd", StringComparison.OrdinalIgnoreCase)
            || key.Contains("workspace", StringComparison.OrdinalIgnoreCase)
            || key.Contains("root", StringComparison.OrdinalIgnoreCase)
            || key.Contains("source", StringComparison.OrdinalIgnoreCase)
            || key.Contains("target", StringComparison.OrdinalIgnoreCase)
            || key.Contains("destination", StringComparison.OrdinalIgnoreCase)
            || key.Contains("output", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ToolPermissionRule>? GetParentSessionRules(
        IAgentExecutionContextAccessor? executionContextAccessor)
    {
        if (executionContextAccessor?.Properties.TryGetValue(
                ContextPropertyKeys.ParentSessionRules, out var value) != true)
        {
            return null;
        }

        return value as IReadOnlyList<ToolPermissionRule>;
    }

    private static string? GetStringArgument(IReadOnlyDictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        return ConvertToString(value);
    }

    private static string? GetAdditionalPropertyString(AIFunction function, string key)
    {
        if (function.AdditionalProperties == null
            || !function.AdditionalProperties.TryGetValue(key, out var value)
            || value == null)
        {
            return null;
        }

        return ConvertToString(value);
    }

    private static string? GetContextPropertyString(
        IAgentExecutionContextAccessor? executionContextAccessor,
        string key)
    {
        if (executionContextAccessor?.Properties.TryGetValue(key, out var value) != true)
        {
            return null;
        }

        return ConvertToString(value);
    }

    private static string? GetMetadataString(
        IReadOnlyDictionary<string, object> metadata,
        string key)
    {
        return metadata.TryGetValue(key, out var value)
            ? ConvertToString(value)
            : null;
    }

    private static string? ConvertToString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            _ => value.ToString()
        };
    }

    private static bool TryConvertToBool(object? value)
    {
        return value switch
        {
            bool flag => flag,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false
        };
    }

    private ToolApprovalRequest BuildRequest(AIFunction function, AIFunctionArguments arguments, ToolApprovalOptions options)
    {
        var argsDict = new Dictionary<string, object?>();
        foreach (var kv in arguments)
        {
            argsDict[kv.Key] = kv.Value;
        }

        return new ToolApprovalRequest
        {
            ToolName = function.Name,
            ToolDescription = function.Description,
            ToolGroup = _toolGroup,
            Arguments = argsDict,
            TimeoutSeconds = options.TimeoutSeconds
        };
    }
}
