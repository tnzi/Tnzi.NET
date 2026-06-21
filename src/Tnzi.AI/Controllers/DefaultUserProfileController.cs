namespace Tnzi.AI.Controllers;

/// <summary>
/// 用户 AI 档案控制器
/// </summary>
[DefaultController]
[ApiAuthorize]
[Route("user-profile")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultUserProfileController : ApiControllerBase
{
    protected readonly IUserProfileService ProfileService;

    public DefaultUserProfileController(IUserProfileService profileService)
    {
        ProfileService = Check.NotNull(profileService);
    }

    /// <summary>
    /// 获取当前用户的 AI 档案
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<UserProfileDto>> Get(CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var result = await ProfileService.GetOrCreateAsync(userId, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新当前用户的 AI 档案
    /// </summary>
    [HttpPut]
    public virtual async Task<ApiResult<UserProfileDto>> Update([FromBody] UpdateUserProfileDto input, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var result = await ProfileService.UpdateAsync(userId, input, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取当前用户 ID，未认证时抛出异常
    /// </summary>
    private Guid GetCurrentUserId() => AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());
}
