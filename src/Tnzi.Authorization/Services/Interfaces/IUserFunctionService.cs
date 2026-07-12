namespace Tnzi.Authorization.Services;

/// <summary>
/// 用户-功能直接授权服务接口
/// 提供不经角色、直接把功能授予（或拒绝）单个用户的管理操作
/// </summary>
/// <remarks>
/// 权限解析为 <c>(角色授权 ∪ 用户直授) − 用户拒绝</c>（用户级优先）；
/// 本服务管理用户直授的 allow 行与 deny 行，角色授权走
/// <see cref="IRoleFunctionService"/>。所有写操作服从与角色路径相同的
/// 委托授权护栏：非超管授权者仅能授出/拒绝自己持有的权限码，且不能
/// 操作"直授配置不被自己权限集包含"的用户。
/// </remarks>
public interface IUserFunctionService
{
    /// <summary>
    /// 获取用户直接授权的功能列表（仅启用的直授行 × 启用的功能）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>功能列表</returns>
    Task<Result<IEnumerable<ModuleFunction>>> GetUserFunctionsAsync(Guid userId);

    /// <summary>
    /// 获取用户直接授权的功能ID列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>功能ID列表</returns>
    Task<Result<IEnumerable<Guid>>> GetUserFunctionIdsAsync(Guid userId);

    /// <summary>
    /// 直接授予功能给用户（增量，已存在的关联跳过）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> AssignFunctionsToUserAsync(Guid userId, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 移除用户的直接授权（不影响其经角色获得的权限）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> RemoveFunctionsFromUserAsync(Guid userId, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 设置用户的直接授权（覆盖原有直授集；落入新集的 deny 行被翻转删除）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> SetUserFunctionsAsync(Guid userId, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 获取用户被否定（deny）的功能ID列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>功能ID列表</returns>
    Task<Result<IEnumerable<Guid>>> GetUserDeniedFunctionIdsAsync(Guid userId);

    /// <summary>
    /// 设置用户的否定权限集（覆盖原有 deny 集；落入新集的 allow 行被翻转
    /// 删除）。deny 行使该用户失去对应权限码——无论哪个角色授予过
    /// （用户级优先，超管不受影响）。传空列表即清空 deny 集。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> SetUserDeniedFunctionsAsync(Guid userId, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 清空用户的所有直接授权（仅 allow 集；deny 集经
    /// <see cref="SetUserDeniedFunctionsAsync"/> 传空列表清空）
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result> ClearUserFunctionsAsync(Guid userId);
}
