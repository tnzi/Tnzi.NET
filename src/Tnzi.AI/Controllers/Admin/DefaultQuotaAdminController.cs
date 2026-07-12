namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 配额管理控制器
/// </summary>
[DefaultController]
[Route("admin/quotas")]
[ApiAuthorize(PermissionName = "ai.quota.view")]
public class DefaultQuotaAdminController : ApiAdminControllerBase
{
    protected readonly IQuotaService QuotaService;
    protected readonly IBudgetService BudgetService;

    public DefaultQuotaAdminController(IQuotaService quotaService, IBudgetService budgetService)
    {
        QuotaService = Check.NotNull(quotaService);
        BudgetService = Check.NotNull(budgetService);
    }

    /// <summary>
    /// 获取用户配额信息
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>配额信息</returns>
    [HttpGet("{userId:guid}")]
    public virtual async Task<ApiResult<UserQuotaDto>> GetQuota(Guid userId, CancellationToken ct = default)
    {
        var result = await QuotaService.GetQuotaAsync(userId, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 分页查询用户配额列表
    /// </summary>
    /// <param name="query">查询参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>分页结果</returns>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<UserQuotaDto>>> GetList([FromBody] UserQuotaQueryDto query, CancellationToken ct = default)
    {
        var result = await QuotaService.GetPagedListAsync(query, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 设置用户配额
    /// </summary>
    /// <param name="request">设置配额请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>配额信息</returns>
    [HttpPost]
    [ApiAuthorize(PermissionName = "ai.quota.update")]
    public virtual async Task<ApiResult<UserQuotaDto>> SetQuota([FromBody] SetQuotaDto request, CancellationToken ct = default)
    {
        var result = await QuotaService.SetQuotaAsync(
            request.UserId,
            request.DailyTokenLimit,
            request.MonthlyTokenLimit,
            request.WarningThreshold,
            request.CriticalThreshold,
            ct);

        return result.ToApiResult();
    }

    /// <summary>
    /// 重置用户配额
    /// </summary>
    /// <param name="request">重置配额请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    [HttpPost("reset")]
    [ApiAuthorize(PermissionName = "ai.quota.update")]
    public virtual async Task<ApiResult> ResetQuota([FromBody] ResetQuotaDto request, CancellationToken ct = default)
    {
        var result = await QuotaService.ResetQuotaAsync(
            request.UserId,
            request.ResetDaily,
            request.ResetMonthly,
            ct);

        return result.ToApiResult();
    }

    /// <summary>
    /// 获取当前周期 USD 预算摘要
    /// </summary>
    /// <param name="tenantId">租户 ID（可选，为空则查看全局）</param>
    /// <param name="startTime">开始时间（可选，默认当月1日）</param>
    /// <param name="endTime">结束时间（可选，默认下月1日）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>预算摘要</returns>
    [HttpGet("budget/summary")]
    public virtual async Task<ApiResult<BudgetSummaryDto>> GetBudgetSummary(
        [FromQuery] Guid? tenantId = null,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var start = startTime ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = endTime ?? start.AddMonths(1);

        var summary = await BudgetService.GetSummaryAsync(tenantId, start, end, ct);
        return Result<BudgetSummaryDto>.Success(summary).ToApiResult();
    }
}
