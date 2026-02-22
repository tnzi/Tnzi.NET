

namespace Tnzi.Identity.Services;

/// <summary>
/// 用户登录记录服务接口
/// </summary>
public interface IUserLoginService
{
    /// <summary>
    /// 记录用户登录
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者（如：Google, Microsoft, GitHub等）</param>
    /// <param name="providerKey">提供者密钥</param>
    /// <param name="displayName">显示名称</param>
    Task RecordLoginAsync(Guid userId, string loginProvider, string providerKey, string? displayName = null);

    /// <summary>
    /// 获取用户的登录记录
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>登录记录列表</returns>
    Task<Result<IEnumerable<UserLoginDto>>> GetUserLoginsAsync(Guid userId);

    /// <summary>
    /// 删除用户登录记录
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="providerKey">提供者密钥</param>
    Task RemoveLoginAsync(Guid userId, string loginProvider, string providerKey);

    /// <summary>
    /// 检查用户是否已绑定指定登录提供者
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="providerKey">提供者密钥</param>
    /// <returns>是否已绑定</returns>
    Task<bool> HasLoginAsync(Guid userId, string loginProvider, string providerKey);
}

