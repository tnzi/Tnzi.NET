namespace Tnzi.Identity.Services;

/// <summary>
/// 「非超管不能替超管做主」的判据：管理员对另一个已有账号动手（写）之前问一次。
/// </summary>
/// <remarks>
/// <para>
/// 重置密码、摘掉第二因子、设登录 IP 允许列表，每一件都等于「决定谁能以这个账号登录」；
/// 改邮箱 / 手机、替地址担保（确认位）、启用 / 停用 / 锁定 / 解锁、删除、改角色成员与组织归属，
/// 每一件都能拼成同一个结果 —— 例如「把超管邮箱改成自己的 + 确认 + 走找回密码」绕开重置密码的护栏，
/// 或者直接停用 / 删除超管。持 <c>user.update</c> / <c>user.delete</c> / <c>user.security</c> 的
/// 普通管理员对超管做这些事，就是借一个比超管小的授权拿下或锁死超管账号。
/// 所以护栏挂在「针对已有账号的每一个管理端写操作」上，而不是挑其中几件。
/// </para>
/// <para>
/// 三种情况不拦：Authorization 模块没加载（没有超管这个概念）、没有调用者上下文（系统 / 播种路径）、
/// 调用者就是目标本人（自助路径另有自己的守卫）。先问目标再问调用者：绝大多数目标不是超管，一次查询就够。
/// </para>
/// </remarks>
internal static class SuperAdminTargetGuard
{
    public const string Message = "Only a super administrator can manage a super administrator's account.";

    /// <summary>调用者无权对目标做这件事时为 true。</summary>
    public static async Task<bool> IsForbiddenAsync(IFunctionAuthorizationService? functionAuthorization, Guid? actorId, Guid targetUserId)
    {
        if (functionAuthorization == null || actorId == null || actorId == Guid.Empty || actorId == targetUserId)
        {
            return false;
        }

        return await functionAuthorization.IsSuperAdminAsync(targetUserId)
            && !await functionAuthorization.IsSuperAdminAsync(actorId.Value);
    }

    /// <summary>
    /// 批量版：目标里只要有一个调用者无权动的超管，整批为 true。批量写不是一个事务，
    /// 所以必须在动第一个之前整批问完，拒绝就一个都不动（不留半批结果）。
    /// </summary>
    public static async Task<bool> IsAnyForbiddenAsync(IFunctionAuthorizationService? functionAuthorization, Guid? actorId, IEnumerable<Guid> targetUserIds)
    {
        if (functionAuthorization == null || actorId == null || actorId == Guid.Empty)
        {
            return false;
        }

        bool? actorIsSuperAdmin = null;
        foreach (var targetId in targetUserIds.Distinct())
        {
            if (targetId == actorId || !await functionAuthorization.IsSuperAdminAsync(targetId))
            {
                continue;
            }

            actorIsSuperAdmin ??= await functionAuthorization.IsSuperAdminAsync(actorId.Value);
            if (actorIsSuperAdmin == false)
            {
                return true;
            }

            // 调用者自己是超管：后面的目标不必再问。
            return false;
        }

        return false;
    }
}
