namespace Tnzi.AI.Controllers;

/// <summary>
/// 用户端用量上限：已认证用户查看自己的用量（只有百分比与重置时间，不给金额）。
/// </summary>
[DefaultController]
[Route("usage-limits")]
[ApiExplorerSettings(GroupName = "user")]
[ApiAuthorize]
public class DefaultUsageLimitController : ApiControllerBase
{
    protected readonly IUsageLimitService UsageLimits;

    public DefaultUsageLimitController(IUsageLimitService usageLimits)
    {
        UsageLimits = Check.NotNull(usageLimits);
    }

    /// <summary>
    /// 当前用户各窗口（5 小时 / 每周）的已用百分比与重置时间。不限的窗口 <c>limited = false</c>、不给百分比。
    /// </summary>
    [HttpGet("me")]
    public virtual async Task<ApiResult<MyUsageLimitsDto>> GetMine(CancellationToken ct = default)
    {
        var userId = AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());
        var status = await UsageLimits.GetMyStatusAsync(userId, ct);
        return Result<MyUsageLimitsDto>.Success(status).ToApiResult();
    }
}
