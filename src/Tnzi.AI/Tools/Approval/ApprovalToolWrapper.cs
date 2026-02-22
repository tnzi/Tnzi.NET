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
    private readonly IToolApprovalHandler _approvalHandler;
    private readonly ToolApprovalOptions _options;
    private readonly ILogger<ApprovalToolWrapper>? _logger;
    private readonly string? _toolGroup;

    /// <summary>
    /// 初始化包装器
    /// </summary>
    /// <param name="toolGroup">工具所属组名（用于 AlwaysRequireApprovalGroups 判断及审批请求）</param>
    public ApprovalToolWrapper(
        AIFunction innerFunction,
        IToolApprovalHandler approvalHandler,
        ToolApprovalOptions options,
        ILogger<ApprovalToolWrapper>? logger = null,
        string? toolGroup = null)
        : base(innerFunction)
    {
        _approvalHandler = Check.NotNull(approvalHandler);
        _options = Check.NotNull(options);
        _logger = logger;
        _toolGroup = toolGroup;
    }

    /// <summary>
    /// 包装工具列表：对需要审批的工具用 ApprovalToolWrapper 包装
    /// </summary>
    /// <param name="tools">原始工具列表</param>
    /// <param name="approvalHandler">审批处理器</param>
    /// <param name="options">审批配置</param>
    /// <param name="logger">可选日志</param>
    /// <param name="toolNameToGroup">工具名到组名的映射（用于 AlwaysRequireApprovalGroups 及审批请求 ToolGroup）</param>
    /// <returns>包装后的工具列表（未启用审批或非 AIFunction 的保持原样）</returns>
    public static IList<AITool> Wrap(
        IList<AITool> tools,
        IToolApprovalHandler approvalHandler,
        ToolApprovalOptions options,
        ILogger<ApprovalToolWrapper>? logger = null,
        IReadOnlyDictionary<string, string>? toolNameToGroup = null)
    {
        if (tools == null || tools.Count == 0)
        {
            return tools ?? (IList<AITool>)new List<AITool>();
        }

        if (!options.Enabled)
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

                if (RequiresApproval(aiFunction.Name, group, options))
                {
                    result.Add(new ApprovalToolWrapper(aiFunction, approvalHandler, options, logger, group));
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
        if (RequiresApproval(InnerFunction.Name, _toolGroup, _options))
        {
            var request = BuildRequest(InnerFunction, arguments, _options);
            _logger?.LogDebug("Requesting approval for tool: {ToolName}", request.ToolName);

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

        return await base.InvokeCoreAsync(arguments, cancellationToken);
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
