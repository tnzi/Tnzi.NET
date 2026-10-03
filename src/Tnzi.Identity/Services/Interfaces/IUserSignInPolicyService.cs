namespace Tnzi.Identity.Services;

/// <summary>
/// 一个账号的登录准入策略（目前只有登录 IP 允许列表）的读与写。管理端用，按用户 id 寻址。
/// </summary>
/// <remarks>
/// <para>
/// <b>执行不在这里</b>。执行是 <see cref="IpAllowListLoginGuard"/>，一条在令牌签发之前运行的
/// <see cref="ILoginGuard"/>。读写与执行分开，守卫才能在没有数据库的情况下逐条验证，
/// 一次界面层面的改动也才不会悄悄改变谁能登录。两边解析列表用的是同一个
/// <see cref="SignInIpAllowList"/>，保证「保存时合法」与「登录时命中」是同一种理解。
/// </para>
/// <para>
/// 越出当前租户范围的账号一律答 404，与不存在相同（<see cref="IUserTenantScopeProvider"/>）。
/// </para>
/// </remarks>
public interface IUserSignInPolicyService
{
    /// <summary>读取策略。没有策略行的账号返回一个全关的 DTO，不是 404。</summary>
    Task<Result<UserSignInPolicyDto>> GetAsync(Guid userId);

    /// <summary>
    /// 改写登录 IP 允许列表。开启时列表必须至少有一个有效条目；任一条目格式不对整次拒绝。
    /// 关闭并清空时删除策略行，让「没有限制」回到「没有行」这一种状态。
    /// </summary>
    Task<Result<UserSignInPolicyDto>> SetIpAllowListAsync(Guid userId, SetIpAllowListDto input);
}
