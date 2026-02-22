namespace Tnzi.AI.Options;

/// <summary>
/// 工具审批配置选项
/// </summary>
public class ToolApprovalOptions
{
    /// <summary>
    /// 是否启用审批机制
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 审批模式
    /// </summary>
    public ToolApprovalMode Mode { get; set; } = ToolApprovalMode.NeverRequire;

    /// <summary>
    /// 始终需要审批的工具名称列表
    /// </summary>
    public List<string> AlwaysRequireApproval { get; set; } = [];

    /// <summary>
    /// 始终需要审批的工具组列表
    /// </summary>
    public List<string> AlwaysRequireApprovalGroups { get; set; } = [];

    /// <summary>
    /// 从不需要审批的工具名称列表
    /// </summary>
    public List<string> NeverRequireApproval { get; set; } = [];

    /// <summary>
    /// 审批超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// 审批模式
/// </summary>
public enum ToolApprovalMode
{
    /// <summary>
    /// 从不需要审批（默认）
    /// </summary>
    NeverRequire = 0,

    /// <summary>
    /// 始终需要审批
    /// </summary>
    AlwaysRequire = 1,

    /// <summary>
    /// 按配置决定（使用 AlwaysRequireApproval/NeverRequireApproval 列表）
    /// </summary>
    Specific = 2
}
