
namespace Tnzi.Security.Authorization;

/// <summary>
/// 功能授权服务接口
/// </summary>
public interface IFunctionAuthorizationService
{
    /// <summary>
    /// 检查用户是否有权限访问指定功能
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="permissionName">权限名称</param>
    /// <returns>是否有权限</returns>
    Task<bool> CheckPermissionAsync(Guid userId, string permissionName);

    /// <summary>
    /// 获取用户的所有权限名称
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>权限名称集合</returns>
    Task<IEnumerable<string>> GetUserPermissionNamesAsync(Guid userId);

    /// <summary>
    /// 一次解析多个用户对同一个权限的授予，返回**持有**该权限的用户 Id 子集。
    /// 语义与逐个 <see cref="CheckPermissionAsync"/> 完全一致（超管旁路、deny-by-default、
    /// 用户级 deny 优先、大小写不敏感），只是把 N 次往返压成常数次。
    /// </summary>
    /// <remarks>
    /// 面向「按用户名单过滤」的调用方：IM 通讯录 / 群成员候选 / 广播受众 / 会话列表。
    /// 这些名单可以很长，而单次判定的成本是至少一次角色查询 —— 逐个循环会把一个
    /// 请求放大成 O(N) 次 DB 往返。默认实现就是那个循环（不打断既有实现），
    /// 真实实现应改写成批量查询。<c>Guid.Empty</c> 与重复 id 会被忽略；空名单或空权限名返回空集。
    /// </remarks>
    /// <param name="userIds">用户 Id 名单</param>
    /// <param name="permissionName">权限名称</param>
    async Task<IReadOnlySet<Guid>> FilterGrantedAsync(IReadOnlyCollection<Guid> userIds, string permissionName)
    {
        var granted = new HashSet<Guid>();
        if (userIds == null || userIds.Count == 0 || string.IsNullOrEmpty(permissionName))
            return granted;

        foreach (var userId in userIds.Where(id => id != Guid.Empty).Distinct())
        {
            if (await CheckPermissionAsync(userId, permissionName))
                granted.Add(userId);
        }

        return granted;
    }

    /// <summary>
    /// 用户是否为超级管理员（绕过一切权限检查、可支配全部角色）。
    /// 默认实现返回 false：没有超管概念的实现把所有用户当普通用户。
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<bool> IsSuperAdminAsync(Guid userId) => Task.FromResult(false);

    /// <summary>
    /// 返回所有超级管理员用户的 Id 集合（<c>SuperAdminRoles</c> 全部角色成员的并集）。
    /// 与 <see cref="IsSuperAdminAsync"/> 对称：后者回答"某一个用户是否超管"（反向单查），
    /// 本方法正向列出全体超管用户，供调用方一次性把超管从<b>面向业务用户</b>的名单
    /// （IM 通讯录、群成员候选、全员通知接收人等）中剔除——超管是系统维护/运维账号，
    /// 按约定不参与业务，不应作为业务联系人对普通用户可见。
    /// 默认实现返回空集：未启用超管概念（如未加载 Authorization 模块）的实现不隐藏任何人。
    /// </summary>
    Task<IReadOnlySet<Guid>> GetSuperAdminUserIdsAsync() =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

    /// <summary>
    /// <paramref name="grantorUserId"/> 是否可支配（管理）指定角色：为其授予/回收权限、
    /// 变更其用户成员。委托规则（权限集包含模型）：超管支配一切角色；其余用户仅能支配
    /// "显式权限集是自己有效权限集子集"的角色，且永远不能支配超管配置角色。
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>默认实现返回 <c>false</c>（fail-closed）。</strong>
    /// 它曾经返回 <c>true</c>，理由是「没有委托语义的实现保持宽松」—— 但那个理由站不住：
    /// 调用方（<c>UserService.GetRoleMembershipViolationAsync</c> 等）已经用
    /// <b>服务为 null</b> 表达「没有权限系统，跳过护栏」这一档；而一个<b>注册了</b>本契约、
    /// 却没有覆写这个方法的实现，表达的是「我不知道谁能支配谁」，那时放行一切
    /// 等于让委托护栏在容器里静默全开，且没有任何症状。
    /// 与同接口上 <c>IsSuperAdminAsync =&gt; false</c> 的方向保持一致。
    /// </remarks>
    /// <param name="grantorUserId">授权者用户ID</param>
    /// <param name="roleId">目标角色ID</param>
    Task<bool> CanManageRoleAsync(Guid grantorUserId, Guid roleId) => Task.FromResult(false);

    /// <summary>
    /// 受保护的角色名 —— 成员自动获得超管旁路的那些角色（<c>Authorization:SuperAdminRoles</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <strong>它存在是为了让「角色定义」这条写路径也能守住超管边界。</strong>
    /// <see cref="CanManageRoleAsync"/> 判定时读的是角色<b>当前的名字</b>，
    /// 于是「先加入一个自己支配得了的普通角色，再把它改名成超管角色名」可以整个绕过它：
    /// 加入那一刻名字还是普通的，改名那一步以前没有任何守卫。
    /// <c>RoleService</c> 拿这个列表在创建 / 改名时直接拒绝目标名。
    /// </para>
    /// <para>
    /// 默认空集：没有加载授权模块的宿主根本没有超管概念，也就没有要保护的名字。
    /// </para>
    /// </remarks>
    IReadOnlyList<string> GetSuperAdminRoleNames() => [];
}