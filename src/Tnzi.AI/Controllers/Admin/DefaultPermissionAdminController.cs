namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 工具权限规则管理控制器
/// 提供规则查询和上下文评估调试端点
/// </summary>
[DefaultController]
[Route("admin/permissions")]
[ApiAuthorize(PermissionName = "ai.permissions.view")]
public class DefaultPermissionAdminController : ApiAdminControllerBase
{
    protected readonly IToolPermissionEvaluator PermissionEvaluator;
    protected readonly IToolPermissionRuleService PermissionRuleService;
    protected readonly IToolRegistry? ToolRegistry;

    /// <summary>
    /// 初始化权限管理控制器
    /// </summary>
    public DefaultPermissionAdminController(
        IToolPermissionEvaluator permissionEvaluator,
        IToolPermissionRuleService permissionRuleService,
        IToolRegistry? toolRegistry = null)
    {
        PermissionEvaluator = Check.NotNull(permissionEvaluator);
        PermissionRuleService = Check.NotNull(permissionRuleService);
        ToolRegistry = toolRegistry;
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
    /// 评估测试 - 给定上下文返回命中的决策结果（调试用）
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
            // 与运行时同源：ApprovalToolWrapper 把工具声明的 IsDestructive 与 shell 分析 OR 合并，
            // 试算也把注册表里的声明 OR 进来，否则 delete_memory 在这里答 Deny、运行时却直接执行
            IsDestructive = request.IsDestructive || IsRegisteredAsDestructive(request.ToolName),
            UserId = request.UserId,
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

    /// <summary>
    /// 获取所有持久化的权限规则
    /// </summary>
    [HttpGet("persisted-rules")]
    public virtual async Task<ApiResult<List<PersistedPermissionRuleDto>>> GetPersistedRules()
    {
        var result = await PermissionRuleService.GetListAsync(HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建持久化权限规则
    /// </summary>
    [HttpPost("persisted-rules")]
    [ApiAuthorize(PermissionName = "ai.permissions.create")]
    public virtual async Task<ApiResult<PersistedPermissionRuleDto>> CreatePersistedRule(
        [FromBody] CreatePersistedPermissionRuleDto input)
    {
        var result = await PermissionRuleService.CreateAsync(input, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新持久化权限规则
    /// </summary>
    [HttpPut("persisted-rules/{id}")]
    [ApiAuthorize(PermissionName = "ai.permissions.update")]
    public virtual async Task<ApiResult<PersistedPermissionRuleDto>> UpdatePersistedRule(
        Guid id,
        [FromBody] CreatePersistedPermissionRuleDto input)
    {
        var result = await PermissionRuleService.UpdateAsync(id, input, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除持久化权限规则
    /// </summary>
    [HttpDelete("persisted-rules/{id}")]
    [ApiAuthorize(PermissionName = "ai.permissions.delete")]
    public virtual async Task<ApiResult> DeletePersistedRule(Guid id)
    {
        var result = await PermissionRuleService.DeleteAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>注册表里该工具是否经 <c>[AIFunction(IsDestructive = true)]</c> 声明为破坏性；没有注册表或查不到时按未声明</summary>
    private bool IsRegisteredAsDestructive(string? toolName)
    {
        if (ToolRegistry == null || string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        return ToolRegistry.GetToolsByNames([toolName]).Any(t => t.IsDestructive);
    }
}
