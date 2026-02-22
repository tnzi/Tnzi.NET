namespace Tnzi.Identity.Services;

/// <summary>
/// 用户详情服务接口
/// </summary>
public interface IUserDetailService
{
    /// <summary>
    /// 根据用户ID获取用户详情
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>用户详情，如果不存在则返回null</returns>
    Task<Result<UserDetailDto>> GetByUserIdAsync(Guid userId);

    /// <summary>
    /// 批量获取用户详情
    /// </summary>
    /// <param name="userIds">用户ID列表</param>
    /// <returns>用户详情映射字典 (UserId -> UserDetailDto)</returns>
    Task<Result<IDictionary<Guid, UserDetailDto>>> GetDetailsByUserIdsAsync(IEnumerable<Guid> userIds);

    /// <summary>
    /// 创建或更新用户详情
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="dto">用户详情信息</param>
    /// <returns>用户详情</returns>
    Task<Result<UserDetailDto>> CreateOrUpdateAsync(Guid userId, CreateUserDetailDto dto);

    /// <summary>
    /// 获取当前登录用户详情
    /// </summary>
    /// <returns>用户详情</returns>
    Task<Result<UserDetailDto>> GetCurrentAsync();

    /// <summary>
    /// 更新当前登录用户详情
    /// </summary>
    /// <param name="dto">用户详情信息</param>
    /// <returns>用户详情</returns>
    Task<Result<UserDetailDto>> UpdateCurrentAsync(CreateUserDetailDto dto);

    /// <summary>
    /// 删除用户详情
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result> DeleteAsync(Guid userId);
}
