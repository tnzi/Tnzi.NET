namespace Tnzi.Identity.Extensions;

/// <summary>
/// UserManager 扩展方法
/// 提供 Guid 类型 userId 的查找方法，消除各服务中的重复代码
/// </summary>
internal static class UserManagerExtensions
{
    /// <summary>
    /// 根据 Guid 类型的用户ID查找用户
    /// </summary>
    internal static Task<User?> FindByGuidAsync(this UserManager<User> userManager, Guid userId)
        => userManager.FindByIdAsync(userId.ToString());

    /// <summary>
    /// 根据手机号查找用户。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <strong><paramref name="requireConfirmed"/> 必须进查询谓词，不能事后过滤。</strong>
    /// 此前是 <c>FirstOrDefault(...)</c> 之后再判 <c>PhoneNumberConfirmed</c>，返回 <c>null</c> ——
    /// 而手机号<b>没有唯一约束</b>，任何人都可以把自己的号码填成别人的。于是一条未验证的
    /// 重复行只要被数据库先返回，真正的号主（号码是已验证的）就再也用不了短信登录与短信找回：
    /// 查询命中的是那条未验证的行，后置过滤把它变成「查无此人」。
    /// </para>
    /// <para>
    /// ★ 排序也是必须的。无 <c>OrderBy</c> 的 <c>FirstOrDefault</c> 取哪一行由数据库决定，
    /// 同一个号码在两次请求里可能落到两个账号上 —— 已验证的优先、同档取最早建的那条，
    /// 结果才是确定的。
    /// </para>
    /// </remarks>
    internal static Task<User?> FindByPhoneNumberAsync(this UserManager<User> userManager, string? phoneNumber, bool requireConfirmed = false)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) return Task.FromResult<User?>(null);

        return userManager.Users
            .Where(u => u.PhoneNumber == phoneNumber && (!requireConfirmed || u.PhoneNumberConfirmed))
            .OrderByDescending(u => u.PhoneNumberConfirmed)
            .ThenBy(u => u.CreationTime)
            .FirstOrDefaultAsync();
    }
}
