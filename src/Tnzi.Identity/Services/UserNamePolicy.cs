namespace Tnzi.Identity.Services;

/// <summary>
/// 用户名与邮箱之间的规则：新账号的用户名怎么定、改邮箱时用户名跟不跟。
/// </summary>
/// <remarks>
/// <para>
/// ★ 两条规则分属两个层次：
/// <list type="bullet">
/// <item><b>建号</b>由 <see cref="TnziSignInOptions.UseEmailAsUserName"/> 决定。开启（默认）时，
/// 有邮箱的账号用户名<b>就是</b>邮箱，调用方传一个不同的值会被拒绝，而不是被悄悄改写或悄悄收下。
/// 收下等于让「用邮箱当用户名」只对不填用户名的入口生效，管理端建号照样各填各的。
/// 关闭时用户名是独立的登录名（工号、昵称式账号），但它<b>不能长得像别人的邮箱</b>。</item>
/// <item><b>跟随</b>与开关无关：用户名等于当前邮箱的账号（两者绑在一起），改邮箱时用户名一起改。
/// 独立用户名的账号不受影响。判据用值而不用开关，是为了让开关中途切换过的部署里，
/// 两种账号各自保持自己的语义。</item>
/// </list>
/// </para>
/// <para>
/// ★★ 「像邮箱的用户名必须是自己的邮箱」是安全约束而不只是整洁：密码登录按
/// 用户名 → 邮箱的顺序解析账号，一个等于他人邮箱的用户名会把对方输入的邮箱解析到自己的账号上。
/// 建号时由本类挡住，存量与其余写入路径由 <see cref="CrossFieldIdentifierValidator"/> 兜底。
/// </para>
/// </remarks>
internal static class UserNamePolicy
{
    /// <summary>含 <c>@</c> 即视为邮箱形态。宽于任何邮箱语法校验，刻意如此：宁可多拦。</summary>
    public static bool IsEmailShaped(string? value) => !string.IsNullOrEmpty(value) && value.Contains('@');

    /// <summary>用户名与邮箱是否绑在一起（值相等，不区分大小写）。</summary>
    public static bool FollowsEmail(User user)
    {
        Check.NotNull(user);
        return !string.IsNullOrEmpty(user.Email)
            && string.Equals(user.UserName, user.Email, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 定下新账号的用户名。返回 <c>null</c> 数据表示「调用方没给、策略也推不出来」，由调用方决定退路
    /// （手机号、生成值，或报必填）。
    /// </summary>
    /// <param name="requested">调用方显式给的用户名，可空。</param>
    /// <param name="email">新账号的邮箱，可空。</param>
    /// <param name="useEmailAsUserName"><see cref="TnziSignInOptions.UseEmailAsUserName"/>。</param>
    public static Result<string?> ResolveForNewAccount(string? requested, string? email, bool useEmailAsUserName)
    {
        var hasRequested = !string.IsNullOrWhiteSpace(requested);
        var hasEmail = !string.IsNullOrWhiteSpace(email);

        if (useEmailAsUserName && hasEmail)
        {
            if (hasRequested && !string.Equals(requested!.Trim(), email!.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<string?>(
                    "This deployment uses the email address as the username. Omit the username or set it to the email address.",
                    400,
                    ErrorCodes.VALIDATION_ERROR);
            }

            // 原样返回而不 Trim：用户名必须与存进 Email 的值逐字一致，否则「是否绑定」的判定会失配
            return Result<string?>.Success(email);
        }

        if (!hasRequested)
        {
            return Result<string?>.Success(null);
        }

        var userName = requested!.Trim();
        if (IsEmailShaped(userName) && !string.Equals(userName, email, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<string?>(
                "A username in email form must be the account's own email address.",
                400,
                ErrorCodes.VALIDATION_ERROR);
        }

        return Result<string?>.Success(userName);
    }
}
