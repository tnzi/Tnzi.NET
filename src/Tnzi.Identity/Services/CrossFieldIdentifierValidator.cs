namespace Tnzi.Identity.Services;

/// <summary>
/// 跨字段标识唯一性：一个账号的用户名不能是另一个账号的邮箱，一个账号的邮箱也不能是另一个账号的用户名。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 密码登录按「用户名 → 邮箱」的顺序解析账号。ASP.NET Identity 只分别保证两列各自唯一，
/// 于是用户名 <c>alice@example.com</c> 可以属于 A、邮箱 <c>alice@example.com</c> 属于 B。
/// B 输入自己的邮箱，解析到的是 A。来路有两条：有人直接把用户名取成别人的邮箱；或者
/// A 的用户名曾经是自己的邮箱、改邮箱时没跟着改，旧地址后来被 B 注册。
/// 两个方向都查，才能让任意一个输入串最多解析到一个账号。
/// </para>
/// <para>
/// 挂在 <see cref="UserManager{TUser}"/> 的校验链上而不是某个服务里：建号、改邮箱、消费方自己
/// 调 <c>UserManager</c> 的写入都要经过它。只拦「与<b>另一个</b>账号冲突」，
/// 不拦「用户名是自己的旧邮箱」这种存量形态，因为那种账号每一次更新（包括登录时重置失败计数）
/// 都要过校验，拦了就等于把它们锁在门外。
/// </para>
/// <para>
/// 已软删除的账号不参与比较：它们登录不了，也不该永久占住一个地址。
/// 比较走 <c>Normalized*</c> 列，与 Identity 自己的唯一性判定同一口径。
/// </para>
/// </remarks>
public sealed class CrossFieldIdentifierValidator : IUserValidator<User>
{
    /// <summary>用户名等于另一个账号的邮箱。</summary>
    public const string UserNameIsAnotherUsersEmail = "UserNameIsAnotherUsersEmail";

    /// <summary>邮箱等于另一个账号的用户名。</summary>
    public const string EmailIsAnotherUsersUserName = "EmailIsAnotherUsersUserName";

    public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user)
    {
        Check.NotNull(manager);
        Check.NotNull(user);

        var errors = new List<IdentityError>();

        if (UserNamePolicy.IsEmailShaped(user.UserName))
        {
            var normalized = manager.NormalizeEmail(user.UserName);
            var taken = manager.SupportsQueryableUsers
                ? await manager.Users.AnyAsync(u => u.Id != user.Id && !u.IsDeleted && u.NormalizedEmail == normalized)
                : IsAnotherLiveUser(await manager.FindByEmailAsync(user.UserName!), user);
            if (taken)
            {
                errors.Add(new IdentityError
                {
                    Code = UserNameIsAnotherUsersEmail,
                    Description = "The username is already in use as another account's email address.",
                });
            }
        }

        if (!string.IsNullOrEmpty(user.Email))
        {
            var normalized = manager.NormalizeName(user.Email);
            var taken = manager.SupportsQueryableUsers
                ? await manager.Users.AnyAsync(u => u.Id != user.Id && !u.IsDeleted && u.NormalizedUserName == normalized)
                : IsAnotherLiveUser(await manager.FindByNameAsync(user.Email), user);
            if (taken)
            {
                errors.Add(new IdentityError
                {
                    Code = EmailIsAnotherUsersUserName,
                    Description = "The email address is already in use as another account's username.",
                });
            }
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    /// <summary>
    /// 不支持 <see cref="IQueryableUserStore{TUser}"/> 的自定义 store 走按键查找的退路：
    /// 直接访问 <c>Users</c> 会抛 <see cref="NotSupportedException"/>，那等于让所有用户写入失败。
    /// </summary>
    private static bool IsAnotherLiveUser(User? found, User user) => found != null && found.Id != user.Id && !found.IsDeleted;
}
