namespace Tnzi.Identity.Extensions;

/// <summary>
/// 改邮箱时让绑定在邮箱上的用户名一起改。
/// </summary>
public static class UserEmailExtensions
{
    /// <summary>
    /// 设置邮箱；若用户名原本等于旧邮箱（两者绑在一起），用户名在<b>同一次更新</b>里改成新邮箱。
    /// 独立用户名的账号只改邮箱，行为与 <see cref="UserManager{TUser}.SetEmailAsync"/> 相同。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 用这个而不是直接调 <c>SetEmailAsync</c>：后者只改邮箱，用户名停在旧地址上且仍能用来登录，
    /// 旧地址日后被别人注册成邮箱时，两个账号就争同一个登录串（见 <c>CrossFieldIdentifierValidator</c>，
    /// 那时它会拒绝后来者，而不是让两人都登录成功）。
    /// </para>
    /// <para>
    /// ★ 两个字段在一次 <c>UpdateUserAsync</c> 里提交：先改内存里的用户名、再走 <c>SetEmailAsync</c>，
    /// 于是用户名唯一性、邮箱唯一性与跨字段校验看到的是同一份新状态，要么都改成、要么都不改。
    /// 分两次调用（先 <c>SetEmailAsync</c> 再 <c>SetUserNameAsync</c>）在中间失败时会留下一个
    /// 用户名是旧邮箱、邮箱是新邮箱的账号。
    /// </para>
    /// </remarks>
    public static async Task<IdentityResult> SetEmailWithUserNameAsync(this UserManager<User> userManager, User user, string? email)
    {
        Check.NotNull(userManager);
        Check.NotNull(user);

        if (string.IsNullOrWhiteSpace(email) || !UserNamePolicy.FollowsEmail(user))
        {
            return await userManager.SetEmailAsync(user, email);
        }

        var previousUserName = user.UserName;
        user.UserName = email;
        var result = await userManager.SetEmailAsync(user, email);
        if (!result.Succeeded)
        {
            user.UserName = previousUserName;
        }

        return result;
    }
}
