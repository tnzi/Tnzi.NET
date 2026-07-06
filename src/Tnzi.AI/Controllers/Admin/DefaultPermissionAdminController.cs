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
    protected readonly IRepository<ToolPermissionRuleEntity, Guid> PermissionRuleRepository;

    /// <summary>
    /// 初始化权限管理控制器
    /// </summary>
    public DefaultPermissionAdminController(
        IToolPermissionEvaluator permissionEvaluator,
        IRepository<ToolPermissionRuleEntity, Guid> permissionRuleRepository)
    {
        PermissionEvaluator = Check.NotNull(permissionEvaluator);
        PermissionRuleRepository = Check.NotNull(permissionRuleRepository);
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

    /// <summary>
    /// 获取所有持久化的权限规则
    /// </summary>
    [HttpGet("persisted-rules")]
    public virtual async Task<ApiResult<List<PersistedPermissionRuleDto>>> GetPersistedRules()
    {
        var entities = await PermissionRuleRepository.AsQueryable()
            .OrderByDescending(e => e.Priority)
            .ThenBy(e => e.Scope)
            .ToListAsync();

        return ApiResult<List<PersistedPermissionRuleDto>>.Ok(entities.MapToList<PersistedPermissionRuleDto>());
    }

    /// <summary>
    /// 创建持久化权限规则
    /// </summary>
    [HttpPost("persisted-rules")]
    public virtual async Task<ApiResult<PersistedPermissionRuleDto>> CreatePersistedRule(
        [FromBody] CreatePersistedPermissionRuleDto input)
    {
        Check.NotNull(input);

        var entity = input.MapTo<ToolPermissionRuleEntity>();

        await PermissionRuleRepository.InsertAsync(entity);
        await PermissionEvaluator.RefreshRulesAsync();

        return ApiResult<PersistedPermissionRuleDto>.Ok(entity.MapTo<PersistedPermissionRuleDto>());
    }

    /// <summary>
    /// 更新持久化权限规则
    /// </summary>
    [HttpPut("persisted-rules/{id}")]
    public virtual async Task<ApiResult<PersistedPermissionRuleDto>> UpdatePersistedRule(
        Guid id,
        [FromBody] CreatePersistedPermissionRuleDto input)
    {
        Check.NotNull(input);

        var entity = await PermissionRuleRepository.GetAsync(id);
        if (entity == null)
        {
            return ApiResult<PersistedPermissionRuleDto>.Error("Permission rule not found.", 404);
        }

        // In-place field assignment — never re-create the entity (would drop the
        // Id / audit fields / TenantId). Behavior & Scope are persisted as int.
        entity.ToolPattern = input.ToolPattern;
        entity.ToolGroup = input.ToolGroup;
        entity.CommandPrefix = input.CommandPrefix;
        entity.ServerName = input.ServerName;
        entity.PathPrefix = input.PathPrefix;
        entity.Behavior = (int)input.Behavior;
        entity.Scope = (int)input.Scope;
        entity.Priority = input.Priority;
        entity.IsDestructiveOnly = input.IsDestructiveOnly;
        entity.IsSubAgentOnly = input.IsSubAgentOnly;
        entity.Reason = input.Reason;
        entity.UserId = input.UserId;
        entity.IsEnabled = input.IsEnabled;

        await PermissionRuleRepository.UpdateAsync(entity);
        await PermissionEvaluator.RefreshRulesAsync();

        return ApiResult<PersistedPermissionRuleDto>.Ok(entity.MapTo<PersistedPermissionRuleDto>());
    }

    /// <summary>
    /// 删除持久化权限规则
    /// </summary>
    [HttpDelete("persisted-rules/{id}")]
    public virtual async Task<ApiResult> DeletePersistedRule(Guid id)
    {
        var entity = await PermissionRuleRepository.GetAsync(id);
        if (entity == null)
        {
            return ApiResult.Error("Permission rule not found.", 404);
        }

        await PermissionRuleRepository.DeleteAsync(entity);

        await PermissionEvaluator.RefreshRulesAsync();

        return ApiResult.Ok();
    }
}
