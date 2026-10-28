namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 管理端用量上限与模型策略：所有人的默认值、某个用户的单独设置与当前用量、计量列表、USD 预算摘要。
/// </summary>
[DefaultController]
[Route("admin/ai/usage-limits")]
[ApiAuthorize(PermissionName = "ai.quota.view")]
public class DefaultUsageLimitAdminController : ApiAdminControllerBase
{
    protected readonly IUsagePolicyService UsagePolicy;
    protected readonly IUsageLimitService UsageLimits;
    protected readonly IBudgetService BudgetService;

    public DefaultUsageLimitAdminController(IUsagePolicyService usagePolicy, IUsageLimitService usageLimits, IBudgetService budgetService)
    {
        UsagePolicy = Check.NotNull(usagePolicy);
        UsageLimits = Check.NotNull(usageLimits);
        BudgetService = Check.NotNull(budgetService);
    }

    /// <summary>所有人的默认值：管理员保存的、配置文件那一层、生效值与模型清单。</summary>
    [HttpGet("defaults")]
    public virtual async Task<ApiResult<UsageLimitDefaultsDto>> GetDefaults(CancellationToken ct = default)
        => (await UsagePolicy.GetDefaultsAsync(ct)).ToApiResult();

    /// <summary>保存所有人的默认值。每一项可以「不设」（跟随配置文件）或设成不限。</summary>
    [HttpPut("defaults")]
    [ApiAuthorize(PermissionName = "ai.quota.update")]
    public virtual async Task<ApiResult<UsageLimitDefaultsDto>> SaveDefaults([FromBody] SaveUsagePolicyDto input, CancellationToken ct = default)
        => (await UsagePolicy.SaveDefaultsAsync(input, ct)).ToApiResult();

    /// <summary>某个用户的单独设置、生效值与当前各窗口的用量（金额与百分比）。</summary>
    [HttpGet("users/{userId:guid}")]
    public virtual async Task<ApiResult<UserUsageLimitsDto>> GetUser(Guid userId, CancellationToken ct = default)
    {
        var policy = await UsagePolicy.GetUserPolicyAsync(userId, ct);
        if (policy.Succeeded && policy.Data is { } dto)
        {
            dto.Usage = await UsageLimits.GetStatusAsync(userId, ct);
        }

        return policy.ToApiResult();
    }

    /// <summary>保存某个用户的单独设置。每一项可以「不设」（跟随默认值）或设成不限。</summary>
    [HttpPut("users/{userId:guid}")]
    [ApiAuthorize(PermissionName = "ai.quota.update")]
    public virtual async Task<ApiResult<UserUsageLimitsDto>> SaveUser(Guid userId, [FromBody] SaveUsagePolicyDto input, CancellationToken ct = default)
    {
        var policy = await UsagePolicy.SaveUserPolicyAsync(userId, input, ct);
        if (policy.Succeeded && policy.Data is { } dto)
        {
            dto.Usage = await UsageLimits.GetStatusAsync(userId, ct);
        }

        return policy.ToApiResult();
    }

    /// <summary>删掉某个用户的单独设置：每一项回到跟随默认值。</summary>
    [HttpDelete("users/{userId:guid}")]
    [ApiAuthorize(PermissionName = "ai.quota.update")]
    public virtual async Task<ApiResult> ClearUser(Guid userId, CancellationToken ct = default)
        => (await UsagePolicy.ClearUserPolicyAsync(userId, ct)).ToApiResult();

    /// <summary>清零某个用户当前的两个窗口（手动放行）；下一次使用重新开窗口。</summary>
    [HttpPost("users/{userId:guid}/reset")]
    [ApiAuthorize(PermissionName = "ai.quota.update")]
    public virtual async Task<ApiResult> ResetUser(Guid userId, CancellationToken ct = default)
        => (await UsageLimits.ResetAsync(userId, ct)).ToApiResult();

    /// <summary>计量列表：用过 AI 的用户与他们当前各窗口的用量，最近用过的在前。</summary>
    [HttpPost("users/query")]
    public virtual async Task<ApiResult<IPagedList<UserUsageSummaryDto>>> GetUsers([FromBody] UsageMeterQueryDto query, CancellationToken ct = default)
        => (await UsageLimits.GetUsersAsync(query, ct)).ToApiResult();

    /// <summary>
    /// 当前周期 USD 预算摘要（<c>AI:Budget</c>，按租户 / Agent 的月度预算，与按用户的用量上限是两回事）。
    /// </summary>
    /// <param name="tenantId">租户 ID（可选，为空则查看全局）</param>
    /// <param name="startTime">开始时间（可选，默认当月1日）</param>
    /// <param name="endTime">结束时间（可选，默认下月1日）</param>
    /// <param name="ct">取消令牌</param>
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
