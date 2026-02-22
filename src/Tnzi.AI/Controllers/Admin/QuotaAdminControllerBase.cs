namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 配额管理控制器基类
/// </summary>
[Route("admin/quotas")]
public abstract class QuotaAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IQuotaService QuotaService;

    protected QuotaAdminControllerBase(IQuotaService quotaService)
    {
        QuotaService = Check.NotNull(quotaService);
    }

    /// <summary>
    /// 获取用户配额信息
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>配额信息</returns>
    [HttpGet("{userId:guid}")]
    public virtual async Task<ApiResult<UserQuotaDto>> GetQuotaAsync(Guid userId, CancellationToken ct = default)
    {
        var result = await QuotaService.GetQuotaAsync(userId, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 设置用户配额
    /// </summary>
    /// <param name="request">设置配额请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>配额信息</returns>
    [HttpPost]
    public virtual async Task<ApiResult<UserQuotaDto>> SetQuotaAsync([FromBody] SetQuotaDto request, CancellationToken ct = default)
    {
        var result = await QuotaService.SetQuotaAsync(
            request.UserId,
            request.DailyTokenLimit,
            request.MonthlyTokenLimit,
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
    public virtual async Task<ApiResult> ResetQuotaAsync([FromBody] ResetQuotaDto request, CancellationToken ct = default)
    {
        var result = await QuotaService.ResetQuotaAsync(
            request.UserId,
            request.ResetDaily,
            request.ResetMonthly,
            ct);

        return result.ToApiResult();
    }
}
