namespace Tnzi.AI.Tools.Approval;

/// <summary>
/// 自动审批处理器 - 默认实现，自动批准所有工具调用
/// </summary>
/// <remarks>
/// <para>
/// 这是 IToolApprovalHandler 的默认实现，自动批准所有工具调用。
/// 用户可以注册自己的实现来实现自定义审批逻辑。
/// </para>
/// <para>
/// 使用示例：
/// <code>
/// // 注册自定义审批处理器
/// services.AddScoped&lt;IToolApprovalHandler, MyCustomApprovalHandler&gt;();
/// </code>
/// </para>
/// </remarks>
public class AutoApprovalHandler : IToolApprovalHandler
{
    private readonly ILogger<AutoApprovalHandler> _logger;

    public AutoApprovalHandler(ILogger<AutoApprovalHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public Task<ToolApprovalResult> RequestApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken ct = default)
    {
        Check.NotNull(request);

        _logger.LogDebug(
            "Auto-approving tool call: {ToolName} with {ArgumentCount} arguments",
            request.ToolName, request.Arguments.Count);

        return Task.FromResult(ToolApprovalResult.AutoApprove());
    }
}
