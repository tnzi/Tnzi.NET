namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 用户详情控制器基类
/// 提供用户详情CRUD等API端点，所有方法支持重写
/// </summary>
public abstract class UserDetailAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IUserDetailService UserDetailService;

    /// <summary>
    /// 初始化用户详情控制器基类
    /// </summary>
    /// <param name="userDetailService">用户详情服务</param>
    protected UserDetailAdminControllerBase(IUserDetailService userDetailService)
    {
        UserDetailService = Check.NotNull(userDetailService);
    }

    /// <summary>
    /// 根据用户ID获取用户详情
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>用户详情</returns>
    [HttpGet("user/{userId}")]
    public virtual async Task<ApiResult<UserDetailDto>> GetByUserId(Guid userId)
    {
        var result = await UserDetailService.GetByUserIdAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建或更新用户详情
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="dto">用户详情信息</param>
    /// <returns>用户详情</returns>
    [HttpPost("user/{userId}")]
    public virtual async Task<ApiResult<UserDetailDto>> CreateOrUpdate(Guid userId, [FromBody] CreateUserDetailDto dto)
    {
        var result = await UserDetailService.CreateOrUpdateAsync(userId, dto);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除用户详情
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("user/{userId}")]
    public virtual async Task<ApiResult> Delete(Guid userId)
    {
        var result = await UserDetailService.DeleteAsync(userId);
        return result.ToApiResult();
    }

}
