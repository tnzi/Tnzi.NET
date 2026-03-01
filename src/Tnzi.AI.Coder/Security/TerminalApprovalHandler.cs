namespace Tnzi.AI.Coder.Security;

/// <summary>
/// 终端交互式审批处理器 — 通过 stdin/stdout 提示用户确认
/// </summary>
/// <remarks>
/// 适用于 CLI 场景。在 Web 场景下应使用 AutoApprovalHandler 或自定义的 WebSocket/SignalR 审批处理器。
/// 支持记忆策略：用户可选择"总是允许此工具"来跳过后续相同工具的审批。
/// </remarks>
public class TerminalApprovalHandler : IToolApprovalHandler
{
    private readonly ILogger<TerminalApprovalHandler> _logger;

    /// <summary>
    /// 记忆：用户选择"总是允许"的工具名称
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _alwaysAllowed = new();

    /// <summary>
    /// 记忆：用户选择"总是允许"的工具组
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _alwaysAllowedGroups = new();

    public TerminalApprovalHandler(ILogger<TerminalApprovalHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<ToolApprovalResult> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken ct = default)
    {
        Check.NotNull(request);

        // 检查是否已在"总是允许"列表中
        if (_alwaysAllowed.ContainsKey(request.ToolName))
        {
            _logger.LogDebug("Tool '{ToolName}' auto-approved (always-allowed)", request.ToolName);
            return ToolApprovalResult.AutoApprove();
        }

        if (!string.IsNullOrEmpty(request.ToolGroup) && _alwaysAllowedGroups.ContainsKey(request.ToolGroup))
        {
            _logger.LogDebug("Tool '{ToolName}' auto-approved (group '{ToolGroup}' always-allowed)", request.ToolName, request.ToolGroup);
            return ToolApprovalResult.AutoApprove();
        }

        // 显示工具信息
        PrintApprovalPrompt(request);

        // 带超时读取用户输入
        var response = await ReadLineWithTimeoutAsync(request.TimeoutSeconds, ct);

        if (response == null)
        {
            _logger.LogInformation("Approval timed out for tool '{ToolName}'", request.ToolName);
            return ToolApprovalResult.Timeout();
        }

        var answer = response.Trim().ToLowerInvariant();

        return answer switch
        {
            "y" or "yes" => HandleApprove(request),
            "a" or "always" => HandleAlwaysAllow(request),
            "g" or "group" => HandleGroupAlwaysAllow(request),
            _ => HandleReject(request)
        };
    }

    /// <summary>
    /// 输出审批提示信息到终端
    /// </summary>
    private static void PrintApprovalPrompt(ToolApprovalRequest request)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n[Approval Required] Tool: {request.ToolName}");
        Console.ResetColor();

        Console.WriteLine($"  Group: {request.ToolGroup ?? "N/A"}");
        Console.WriteLine($"  Reason: {request.Reason ?? "N/A"}");

        if (request.Arguments.Count > 0)
        {
            Console.WriteLine("  Arguments:");
            foreach (var (key, value) in request.Arguments)
            {
                var displayValue = value?.ToString() ?? "null";
                if (displayValue.Length > 200)
                {
                    displayValue = displayValue[..200] + "...";
                }
                Console.WriteLine($"    {key}: {displayValue}");
            }
        }

        Console.Write("\n  [Y]es / [N]o / [A]lways allow this tool / [G]roup always allow: ");
    }

    /// <summary>
    /// 带超时的终端行读取
    /// </summary>
    private static async Task<string?> ReadLineWithTimeoutAsync(int timeoutSeconds, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            // 在线程池中执行阻塞的 Console.ReadLine
            var result = await Task.Run(() =>
            {
                try
                {
                    // 轮询检查取消状态，避免 Console.ReadLine 无限阻塞
                    while (!cts.Token.IsCancellationRequested)
                    {
                        if (Console.KeyAvailable)
                        {
                            return Console.ReadLine();
                        }

                        Thread.Sleep(100);
                    }

                    return null;
                }
                catch (InvalidOperationException)
                {
                    // 非交互式终端，回退到阻塞读取
                    return Console.ReadLine();
                }
            }, cts.Token);

            return result;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(); // 换行
            return null;
        }
    }

    private ToolApprovalResult HandleApprove(ToolApprovalRequest request)
    {
        _logger.LogDebug("Tool '{ToolName}' approved by user", request.ToolName);
        return ToolApprovalResult.Approve("terminal-user");
    }

    private ToolApprovalResult HandleAlwaysAllow(ToolApprovalRequest request)
    {
        _alwaysAllowed.TryAdd(request.ToolName, true);
        _logger.LogInformation("Tool '{ToolName}' added to always-allow list", request.ToolName);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Tool '{request.ToolName}' will be auto-approved from now on.");
        Console.ResetColor();

        return ToolApprovalResult.Approve("terminal-user");
    }

    private ToolApprovalResult HandleGroupAlwaysAllow(ToolApprovalRequest request)
    {
        if (string.IsNullOrEmpty(request.ToolGroup))
        {
            // 没有工具组，退化为单工具总是允许
            return HandleAlwaysAllow(request);
        }

        _alwaysAllowedGroups.TryAdd(request.ToolGroup, true);
        _logger.LogInformation("Tool group '{ToolGroup}' added to always-allow list", request.ToolGroup);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  All tools in group '{request.ToolGroup}' will be auto-approved from now on.");
        Console.ResetColor();

        return ToolApprovalResult.Approve("terminal-user");
    }

    private ToolApprovalResult HandleReject(ToolApprovalRequest request)
    {
        _logger.LogDebug("Tool '{ToolName}' rejected by user", request.ToolName);
        return ToolApprovalResult.Reject("User denied", "terminal-user");
    }
}
