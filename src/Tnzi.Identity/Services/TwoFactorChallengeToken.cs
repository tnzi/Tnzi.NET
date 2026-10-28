namespace Tnzi.Identity.Services;

/// <summary>
/// 2FA 挑战的临时令牌：随机部分 + 这一次挑战<b>实际提供</b>的方式。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <strong>第二步只能用挑战提供过的方式完成，而那份列表必须跟着令牌走。</strong>
/// 验证码登录与 passkey 登录会把本次已经证明过的因子从挑战里扣掉（再问一次问的是同一件事），
/// 于是挑战列表比「用户已启用的全部方式」少一项。第二步的三个入口（发码 / 验码 / passkey 断言）
/// 此前各自按「用户已启用 ∩ 渠道开着」重算一遍完整集合来校验，被扣掉的那一项照样在里面：
/// 只控制了受害者邮箱的人，先用邮箱验证码登录拿到只要求 TOTP 的挑战，再对同一枚临时令牌
/// 选 Email 发码、验码，就拿到了完整令牌：两步验证退化成邮箱单因子。
/// </para>
/// <para>
/// ★ <strong>为什么编码进令牌本身，而不是另存一行。</strong>临时令牌按用户 upsert
/// （唯一键 <c>(UserId, TwoFactor, TempToken, SessionId)</c>，同一用户同时只有一枚），
/// 另存一行「这枚令牌提供了哪些方式」是两次独立写入：同一用户的两次挑战交错时，
/// 会留下 A 的令牌配 B 的列表。写进令牌值就是一次写入，而库里只存整枚令牌的哈希、
/// 按哈希查找：改动后缀等于换了一枚库里没有的令牌，查不到就是拒绝。
/// 所以服务端从<b>查得到的</b>令牌里读出来的列表，就是当初签发时写进去的那份。
/// </para>
/// <para>
/// 列表不是秘密：同一份 <c>supportedTypes</c> 本来就在 403 挑战的响应体里。
/// 前端把临时令牌当不透明字符串原样回传，格式变化对它不可见。
/// </para>
/// </remarks>
internal static class TwoFactorChallengeToken
{
    /// <summary>随机部分与方式列表之间的分隔符。URL 安全的 Base64 字母表里没有它，所以不会与随机部分混淆。</summary>
    private const char Separator = '.';

    /// <summary>方式之间的分隔符。</summary>
    private const char TypeSeparator = '-';

    /// <summary>随机部分的字节数（256 位）。</summary>
    private const int RandomBytes = 32;

    /// <summary>为一次挑战签发临时令牌，把提供的方式写进令牌。</summary>
    /// <param name="offered">本次挑战提供的方式；不可为空集合（没有可选方式的挑战不该发出）。</param>
    public static string Create(IEnumerable<TwoFactorType> offered)
    {
        var types = Check.NotNull(offered).Distinct().OrderBy(t => t).ToList();
        if (types.Count == 0)
        {
            throw new ArgumentException("A two-factor challenge must offer at least one method.", nameof(offered));
        }

        // URL 安全的字母表（不含 / + =）：令牌由前端原样回传，进了 URL 的标准 Base64 会被转义或改写，按哈希就查不到了。
        var random = OneTimeToken.Create(RandomBytes);
        var encodedTypes = string.Join(TypeSeparator, types.Select(t => ((int)t).ToString(CultureInfo.InvariantCulture)));
        return $"{random}{Separator}{encodedTypes}";
    }

    /// <summary>
    /// 读出令牌签发时提供的方式。格式不对、含未知方式或为空时返回 <c>null</c>，调用方按无效令牌拒绝。
    /// </summary>
    /// <remarks>
    /// ⚠ 只对<b>已经按哈希查到</b>的令牌调用：查不到的令牌后缀是谁写的都有可能。
    /// </remarks>
    public static IReadOnlySet<TwoFactorType>? ReadOffered(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var separatorIndex = token.LastIndexOf(Separator);
        if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
        {
            return null;
        }

        var offered = new HashSet<TwoFactorType>();
        foreach (var part in token[(separatorIndex + 1)..].Split(TypeSeparator))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var raw)
                || !Enum.IsDefined(typeof(TwoFactorType), raw))
            {
                return null;
            }

            offered.Add((TwoFactorType)raw);
        }

        return offered.Count == 0 ? null : offered;
    }
}
