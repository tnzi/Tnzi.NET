

namespace Tnzi.Identity.Services;

/// <summary>
/// 认证令牌服务接口
/// </summary>
public interface IAuthTokenService
{
    /// <summary>
    /// 保存令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="name">令牌名称</param>
    /// <param name="value">令牌值</param>
    /// <param name="expiresAt">过期时间</param>
    /// <returns>令牌ID</returns>
    Task<Guid> SaveTokenAsync(Guid userId, string loginProvider, string name, string value, DateTime? expiresAt = null);

    /// <summary>
    /// 获取令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="name">令牌名称</param>
    /// <returns>令牌值</returns>
    Task<string?> GetTokenAsync(Guid userId, string loginProvider, string name);

    /// <summary>
    /// 删除令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="name">令牌名称</param>
    Task RemoveTokenAsync(Guid userId, string loginProvider, string name);

    /// <summary>
    /// 删除用户的所有令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者（可选）</param>
    Task RemoveAllTokensAsync(Guid userId, string? loginProvider = null);

    /// <summary>
    /// 标记令牌为已使用（带并发控制，已被使用的令牌返回 false）
    /// </summary>
    /// <param name="tokenId">令牌ID</param>
    /// <returns>标记成功返回 true；令牌不存在或已被使用返回 false</returns>
    Task<bool> MarkTokenAsUsedAsync(Guid tokenId);

    /// <summary>
    /// 清理过期的令牌
    /// </summary>
    /// <returns>清理的令牌数量</returns>
    Task<int> CleanExpiredTokensAsync();

    /// <summary>
    /// 获取用户的所有令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者（可选）</param>
    /// <returns>令牌列表</returns>
    Task<IEnumerable<AuthToken>> GetUserTokensAsync(Guid userId, string? loginProvider = null);

    /// <summary>
    /// 通过令牌值查找令牌（用于临时Token查找用户）
    /// </summary>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="name">令牌名称</param>
    /// <param name="value">令牌值</param>
    /// <returns>令牌实体，如果不存在则返回null</returns>
    Task<AuthToken?> FindTokenByValueAsync(string loginProvider, string name, string value);
}

