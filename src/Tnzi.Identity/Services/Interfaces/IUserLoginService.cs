

namespace Tnzi.Identity.Services;

/// <summary>
/// 用户登录记录服务接口：查询账号关联了哪些第三方登录。
/// </summary>
/// <remarks>
/// 只读。关联与解除关联走 <c>UserManager.AddLoginAsync</c> / <c>RemoveLoginAsync</c>（OAuth 服务里），
/// 它们写的就是这里读的那张表；另开一条写入口只会在同一行上重复写一遍。
/// </remarks>
public interface IUserLoginService
{
    /// <summary>
    /// 获取用户的登录记录
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>登录记录列表</returns>
    Task<Result<IEnumerable<UserLoginDto>>> GetUserLoginsAsync(Guid userId);

    /// <summary>
    /// 检查用户是否已绑定指定登录提供者
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="loginProvider">登录提供者</param>
    /// <param name="providerKey">提供者密钥</param>
    /// <returns>是否已绑定</returns>
    Task<bool> HasLoginAsync(Guid userId, string loginProvider, string providerKey);
}

