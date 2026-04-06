namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 工具权限规则管理控制器
/// 提供规则查询和上下文评估调试端点
/// </summary>
[DefaultController]
[Route("admin/permissions")]
[ApiExplorerSettings(GroupName = "admin")]
public class DefaultPermissionAdminController : ApiAdminControllerBase
{
    protected readonly IToolPermissionEvaluator PermissionEvaluator;

    /// <summary>
    /// 初始化权限管理控制器
    /// </summary>
    public DefaultPermissionAdminController(IToolPermissionEvaluator permissionEvaluator)
    {
        PermissionEvaluator = Check.NotNull(permissionEvaluator);
    }

    /// <summary>
    /// 获取当前所有 Session 级别规则
    /// </summary>
    [HttpGet("rules")]
    public virtual ApiResult<PermissionRulesDto> GetRules()
    {
        var sessionRules = PermissionEvaluator.GetSessionRules();
        var dto = new PermissionRulesDto
        {
            HasRules = PermissionEvaluator.HasRules,
            SessionRules = sessionRules.Select(r => new PermissionRuleItemDto
            {
                ToolPattern = r.ToolPattern,
                ToolGroup = r.ToolGroup,
                CommandPrefix = r.CommandPrefix,
                ServerName = r.ServerName,
                PathPrefix = r.PathPrefix,
                IsSubAgentOnly = r.IsSubAgentOnly,
                SubAgentName = r.SubAgentName,
                IsWorkflowOnly = r.IsWorkflowOnly,
                WorkflowNodeName = r.WorkflowNodeName,
                Behavior = r.Behavior,
                Scope = r.Scope,
                Priority = r.Priority,
                IsDestructiveOnly = r.IsDestructiveOnly,
                Reason = r.Reason
            }).ToList()
        };

        return ApiResult<PermissionRulesDto>.Ok(dto);
    }

    /// <summary>
    /// 评估测试 — 给定上下文返回命中的决策结果（调试用）
    /// </summary>
    [HttpPost("rules/evaluate")]
    public virtual ApiResult<PermissionEvaluateResultDto> Evaluate([FromBody] PermissionEvaluateRequestDto request)
    {
        Check.NotNull(request);

        var context = new ToolPermissionContext
        {
            ToolName = request.ToolName ?? string.Empty,
            ToolGroup = request.ToolGroup,
            WorkingDirectory = request.WorkingDirectory,
            CandidatePaths = request.CandidatePaths ?? [],
            ServerName = request.ServerName,
            IsSubAgent = request.IsSubAgent,
            SubAgentName = request.SubAgentName,
            IsWorkflowRun = request.IsWorkflowRun,
            WorkflowId = request.WorkflowId,
            WorkflowExecutionId = request.WorkflowExecutionId,
            WorkflowNodeName = request.WorkflowNodeName,
            ShellCommand = request.ShellCommand,
            IsDestructive = request.IsDestructive,
            Arguments = request.Arguments ?? new Dictionary<string, object?>()
        };

        var decision = PermissionEvaluator.Evaluate(context);

        var dto = new PermissionEvaluateResultDto
        {
            ToolName = decision.ToolName,
            Behavior = decision.Behavior,
            Reason = decision.Reason,
            Scope = decision.Scope,
            MatchedRulePattern = decision.MatchedRulePattern,
            MatchedToolGroup = decision.MatchedToolGroup,
            MatchedServerName = decision.MatchedServerName,
            MatchedPathPrefix = decision.MatchedPathPrefix,
            MatchedSubAgentName = decision.MatchedSubAgentName,
            MatchedWorkflowNodeName = decision.MatchedWorkflowNodeName
        };

        return ApiResult<PermissionEvaluateResultDto>.Ok(dto);
    }
}

/// <summary>
/// 权限规则列表响应
/// </summary>
public class PermissionRulesDto
{
    /// <summary>评估器是否包含任何规则</summary>
    public bool HasRules { get; set; }

    /// <summary>当前 Session 级别规则</summary>
    public List<PermissionRuleItemDto> SessionRules { get; set; } = [];
}

/// <summary>
/// 权限规则条目
/// </summary>
public class PermissionRuleItemDto
{
    public string ToolPattern { get; set; } = "*";
    public string? ToolGroup { get; set; }
    public string? CommandPrefix { get; set; }
    public string? ServerName { get; set; }
    public string? PathPrefix { get; set; }
    public bool IsSubAgentOnly { get; set; }
    public string? SubAgentName { get; set; }
    public bool IsWorkflowOnly { get; set; }
    public string? WorkflowNodeName { get; set; }
    public PermissionBehavior Behavior { get; set; }
    public ToolPermissionScope Scope { get; set; }
    public int Priority { get; set; }
    public bool IsDestructiveOnly { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// 权限评估测试请求
/// </summary>
public class PermissionEvaluateRequestDto
{
    /// <summary>工具名称</summary>
    [Required]
    public string? ToolName { get; set; }

    /// <summary>工具组</summary>
    public string? ToolGroup { get; set; }

    /// <summary>工作目录</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>候选路径</summary>
    public List<string>? CandidatePaths { get; set; }

    /// <summary>MCP server 名称</summary>
    public string? ServerName { get; set; }

    /// <summary>是否子 Agent</summary>
    public bool IsSubAgent { get; set; }

    /// <summary>子 Agent 名称</summary>
    public string? SubAgentName { get; set; }

    /// <summary>是否 workflow 运行</summary>
    public bool IsWorkflowRun { get; set; }

    /// <summary>workflow 定义 ID</summary>
    public Guid? WorkflowId { get; set; }

    /// <summary>workflow 执行 ID</summary>
    public string? WorkflowExecutionId { get; set; }

    /// <summary>workflow 节点名称</summary>
    public string? WorkflowNodeName { get; set; }

    /// <summary>Shell 命令</summary>
    public string? ShellCommand { get; set; }

    /// <summary>是否破坏性工具</summary>
    public bool IsDestructive { get; set; }

    /// <summary>工具参数</summary>
    public Dictionary<string, object?>? Arguments { get; set; }
}

/// <summary>
/// 权限评估结果
/// </summary>
public class PermissionEvaluateResultDto
{
    public string ToolName { get; set; } = string.Empty;
    public PermissionBehavior Behavior { get; set; }
    public string? Reason { get; set; }
    public ToolPermissionScope? Scope { get; set; }
    public string? MatchedRulePattern { get; set; }
    public string? MatchedToolGroup { get; set; }
    public string? MatchedServerName { get; set; }
    public string? MatchedPathPrefix { get; set; }
    public string? MatchedSubAgentName { get; set; }
    public string? MatchedWorkflowNodeName { get; set; }
}
