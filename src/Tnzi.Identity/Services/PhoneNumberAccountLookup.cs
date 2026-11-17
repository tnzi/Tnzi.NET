namespace Tnzi.Identity.Services;

/// <summary>
/// 按手机号找账号的结果：查无 / 恰好一个 / 不止一个。
/// </summary>
public enum PhoneNumberMatch
{
    /// <summary>没有符合条件的账号。</summary>
    None = 0,

    /// <summary>恰好一个账号持有这个号码，<see cref="PhoneNumberAccountLookup.User"/> 就是它。</summary>
    Unique = 1,

    /// <summary>不止一个账号持有这个号码。调用方必须拒绝，绝不能替用户挑一个。</summary>
    Ambiguous = 2,
}

/// <summary>
/// 按手机号解析账号的唯一入口。凡是「拿一个号码去认出一个账号」的路径（短信验证码登录、
/// 密码表单里把手机号当账号名、短信找回密码……）都经它，而不是自己写 <c>Where(PhoneNumber == …)</c>。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <strong>手机号不唯一，而且在一些部署里本来就是共用的</strong>（事务所总机、
/// 替别人收两步验证码的助理）。那里号码只用来收第二因子，从来不该拿来认人。
/// 此前的查找在多个账号同号时「已验证优先、同档取最早」挑一个 —— 于是新员工的号码一旦
/// 也登记在管理员账号上，用这个号码做短信验证码登录就会登进管理员账号。
/// 这里只给出三种回答，<see cref="PhoneNumberMatch.Ambiguous"/> 时<b>不带任何账号</b>，
/// 调用方拿不到一个可以「先凑合用」的结果。
/// </para>
/// <para>
/// ★ <strong>「不止一个」按分档计数：已验证的号码一档，未验证的一档，只在决定结果的那一档里数。</strong>
/// 已验证的账号证明过自己收得到这个号码上的码；未验证的只是有人把号码填了进去。
/// 若把未验证的也算进来，任何能给某个账号写一个未验证号码的人（管理端录入、导入、注册）
/// 就能让真号主的短信登录与短信找回一起失效 —— 这正是本模块此前修过的那类拒绝服务。
/// 所以：已验证的有两个及以上 ⇒ 歧义；恰好一个 ⇒ 就是它，未验证的同号行不影响；
/// 一个已验证的都没有、且调用方允许未验证号码时，再在未验证那一档里同样判定。
/// </para>
/// <para>
/// 「活着的账号」= 未被软删除的账号（由全局软删过滤器保证）。被锁定或停用的账号<b>照样计数</b>：
/// 锁定是暂时的，把它排除掉会让同一个号码在锁定期间解析到另一个账号上，
/// 而那个账号会被登录守卫自己拒绝，不需要在这里替它让位。
/// </para>
/// </remarks>
public sealed record PhoneNumberAccountLookup
{
    /// <summary>
    /// 歧义时回给用户的话：说清楚为什么不行、该怎么办。
    /// </summary>
    public const string AmbiguousMessage =
        "This phone number is registered to more than one account, so it cannot identify yours. Use your email address or username instead.";

    private PhoneNumberAccountLookup(PhoneNumberMatch match, User? user)
    {
        Match = match;
        User = user;
    }

    /// <summary>解析结果。</summary>
    public PhoneNumberMatch Match { get; }

    /// <summary>仅当 <see cref="Match"/> 为 <see cref="PhoneNumberMatch.Unique"/> 时非 null。</summary>
    public User? User { get; }

    /// <summary>是否不止一个账号持有这个号码。</summary>
    public bool IsAmbiguous => Match == PhoneNumberMatch.Ambiguous;

    /// <summary>查无。</summary>
    public static PhoneNumberAccountLookup None { get; } = new(PhoneNumberMatch.None, null);

    /// <summary>不止一个。</summary>
    public static PhoneNumberAccountLookup Ambiguous { get; } = new(PhoneNumberMatch.Ambiguous, null);

    /// <summary>恰好一个。</summary>
    public static PhoneNumberAccountLookup Unique(User user) => new(PhoneNumberMatch.Unique, Check.NotNull(user));

    /// <summary>
    /// 在给定的账号集合里按号码解析。
    /// </summary>
    /// <param name="users">账号查询源（通常是 <c>UserManager.Users</c>）。</param>
    /// <param name="phoneNumber">要解析的号码，按存储形态精确匹配。</param>
    /// <param name="requireConfirmed">为 true 时只看已验证的号码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<PhoneNumberAccountLookup> ResolveAsync(IQueryable<User> users, string? phoneNumber, bool requireConfirmed, CancellationToken cancellationToken = default)
    {
        Check.NotNull(users);
        if (string.IsNullOrWhiteSpace(phoneNumber)) return None;

        var confirmed = await TakeTwoAsync(users, phoneNumber, confirmed: true, cancellationToken);
        if (confirmed.Count > 0 || requireConfirmed) return FromCandidates(confirmed);

        return FromCandidates(await TakeTwoAsync(users, phoneNumber, confirmed: false, cancellationToken));
    }

    // 只取两行就够判定：第二行存在即歧义，不需要知道到底有多少个。
    private static Task<List<User>> TakeTwoAsync(IQueryable<User> users, string phoneNumber, bool confirmed, CancellationToken cancellationToken)
        => users
            .Where(u => u.PhoneNumber == phoneNumber && u.PhoneNumberConfirmed == confirmed)
            .OrderBy(u => u.CreationTime)
            .Take(2)
            .ToListAsync(cancellationToken);

    private static PhoneNumberAccountLookup FromCandidates(List<User> candidates) => candidates.Count switch
    {
        0 => None,
        1 => Unique(candidates[0]),
        _ => Ambiguous,
    };
}
