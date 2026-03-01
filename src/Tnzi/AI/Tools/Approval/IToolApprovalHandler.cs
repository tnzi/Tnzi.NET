namespace Tnzi.AI.Tools.Approval;

/// <summary>
/// 工具调用审批处理器接口
/// </summary>
/// <remarks>
/// <para>
/// 定义工具调用审批的标准接口，用于实现 human-in-the-loop 场景。
/// 当工具配置为需要审批时，在执行前会调用此处理器。
/// </para>
/// <para>
/// 使用场景：
/// <list type="bullet">
/// <item><description>高风险操作（如删除文件、执行系统命令）</description></item>
/// <item><description>涉及敏感数据的操作</description></item>
/// <item><description>需要人工确认的业务流程</description></item>
/// </list>
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public interface IToolApprovalHandler
{
    /// <summary>
    /// 请求工具调用审批
    /// </summary>
    /// <param name="request">审批请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>审批结果</returns>
    Task<ToolApprovalResult> RequestApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// 工具调用审批请求
/// </summary>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public class ToolApprovalRequest
{
    /// <summary>
    /// 默认审批超时时间（秒）
    /// </summary>
    public const int DefaultTimeoutSeconds = 300;

    /// <summary>
    /// 请求 ID
    /// </summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// 工具描述
    /// </summary>
    public string? ToolDescription { get; set; }

    /// <summary>
    /// 工具组名称
    /// </summary>
    public string? ToolGroup { get; set; }

    /// <summary>
    /// 调用参数
    /// </summary>
    public Dictionary<string, object?> Arguments { get; set; } = [];

    /// <summary>
    /// 调用上下文（如线程 ID、用户 ID 等）
    /// </summary>
    public Dictionary<string, string> Context { get; set; } = [];

    /// <summary>
    /// AI 生成的调用理由
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 请求时间
    /// </summary>
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 审批超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
}

/// <summary>
/// 工具调用审批结果
/// </summary>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public class ToolApprovalResult
{
    /// <summary>
    /// 是否批准
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>
    /// 审批状态
    /// </summary>
    public ToolApprovalStatus Status { get; set; }

    /// <summary>
    /// 审批人
    /// </summary>
    public string? ApprovedBy { get; set; }

    /// <summary>
    /// 拒绝人
    /// </summary>
    public string? RejectedBy { get; set; }

    /// <summary>
    /// 审批时间
    /// </summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>
    /// 拒绝理由
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>
    /// 修改后的参数（审批人可能修改参数）
    /// </summary>
    public Dictionary<string, object?>? ModifiedArguments { get; set; }

    /// <summary>
    /// 创建批准结果
    /// </summary>
    public static ToolApprovalResult Approve(string? approvedBy = null)
    {
        return new ToolApprovalResult
        {
            Approved = true,
            Status = ToolApprovalStatus.Approved,
            ApprovedBy = approvedBy,
            ApprovedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 创建拒绝结果
    /// </summary>
    public static ToolApprovalResult Reject(string? reason = null, string? rejectedBy = null)
    {
        return new ToolApprovalResult
        {
            Approved = false,
            Status = ToolApprovalStatus.Rejected,
            RejectedBy = rejectedBy,
            ApprovedBy = null,
            ApprovedAt = DateTimeOffset.UtcNow,
            RejectionReason = reason
        };
    }

    /// <summary>
    /// 创建超时结果
    /// </summary>
    public static ToolApprovalResult Timeout()
    {
        return new ToolApprovalResult
        {
            Approved = false,
            Status = ToolApprovalStatus.Timeout
        };
    }

    /// <summary>
    /// 创建自动批准结果
    /// </summary>
    public static ToolApprovalResult AutoApprove()
    {
        return new ToolApprovalResult
        {
            Approved = true,
            Status = ToolApprovalStatus.AutoApproved,
            ApprovedAt = DateTimeOffset.UtcNow
        };
    }
}

/// <summary>
/// 审批状态
/// </summary>
public enum ToolApprovalStatus
{
    /// <summary>
    /// 待审批
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 已批准
    /// </summary>
    Approved = 1,

    /// <summary>
    /// 已拒绝
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// 超时
    /// </summary>
    Timeout = 3,

    /// <summary>
    /// 自动批准（无需审批）
    /// </summary>
    AutoApproved = 4
}
