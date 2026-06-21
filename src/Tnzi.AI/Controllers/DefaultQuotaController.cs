namespace Tnzi.AI.Controllers;

/// <summary>
/// 用户端配额控制器 — 已认证用户查看自己的 AI 使用配额。
/// </summary>
[DefaultController]
[Route("quotas")]
[ApiExplorerSettings(GroupName = "user")]
[ApiAuthorize]
public class DefaultQuotaController : ApiControllerBase
{
    protected readonly IQuotaService QuotaService;

    public DefaultQuotaController(IQuotaService quotaService)
    {
        QuotaService = Check.NotNull(quotaService);
    }

    /// <summary>
    /// 获取当前用户的配额信息（剩余每日/每月额度）
    /// </summary>
    [HttpGet("me")]
    public virtual async Task<ApiResult<UserQuotaDto>> GetMyQuota(CancellationToken ct = default)
    {
        var userId = AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());
        var result = await QuotaService.GetQuotaAsync(userId, ct);
        return result.ToApiResult();
    }
}
