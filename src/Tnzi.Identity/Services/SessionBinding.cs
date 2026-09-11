namespace Tnzi.Identity.Services;

/// <summary>
/// 会话与客户端特征的绑定算法：把一串 User-Agent 归一化成一枚可比对的指纹。
/// </summary>
/// <remarks>
/// <para>
/// <b>要解决的问题是「比什么算变了」。</b>OWASP <i>Cookie Theft Mitigation</i> 的说法是
/// 比对「含义有没有发生显著变化」，而不是逐字节相等 —— 因为逐字节相等会被浏览器的
/// 自动更新打败：Chrome 每四周升一个大版本，UA 串跟着变一个数字，一条 7 天的会话
/// 于是有可观的概率在用户什么都没做的情况下被判成「被盗」。这类误报的代价不是
/// 「多登一次」，而是安全控制会被部署方关掉。
/// </para>
/// <para>
/// ★ <b>归一化方式是把所有数字串抹掉</b>，不是解析 UA。理由有两条：
/// ① 版本号是 UA 里唯一会自己变的部分，抹掉它就抹掉了全部已知误报源，
///    而浏览器名、渲染引擎、操作系统、设备类型都是非数字的，一个都不会丢；
/// ② 解析 UA 需要 <c>IUserAgentParserService</c>，而它是<b>可选</b>依赖 ——
///    让一道安全检查的判据随「有没有注册某个可选服务」而变，等于同一份配置在两个
///    部署上给出不同的安全强度，且没有任何地方看得出来。这里刻意只用纯函数。
/// </para>
/// <para>
/// 能挡住的：令牌被搬到另一台机器 / 另一个浏览器 / 另一个操作系统上使用 ——
/// 这正是信息窃取类恶意软件的常规形态（在攻击者自己的机器上复用被盗令牌）。
/// 挡不住的：攻击者刻意复刻受害者的 UA。那需要设备级绑定（DBSC / DPoP），
/// 不在本算法的射程内，也不该由它假装覆盖。
/// </para>
/// </remarks>
public static class SessionBinding
{
    /// <summary>
    /// 计算 User-Agent 的绑定指纹；<paramref name="userAgent"/> 为空白时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 空白返回 <c>null</c> 而不是某个固定值，是为了让「没有采集到 UA」与
    /// 「UA 是空串」在比对时都落到「无从比对」这一档，由调用方按放行处理 ——
    /// 把两个都没有 UA 的请求判成「指纹相同」是能工作的，但把「没采集到」
    /// 当作一次成功的身份比对，是在给一道不存在的检查记功。
    /// </remarks>
    public static string? Fingerprint(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var normalized = Normalize(userAgent);
        return normalized.Length == 0
            ? null
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    /// <summary>
    /// 判断两个 User-Agent 是否指向同一个客户端。
    /// </summary>
    /// <remarks>
    /// ★ <b>任意一侧取不到 UA 时返回 <c>true</c>（放行）。</b>会话可能建立于本特性上线之前，
    /// 也可能来自刻意不采集 UA 的部署或非浏览器客户端。这些情况下没有可比的东西，
    /// 而「比不了」不等于「不匹配」—— 把它判成不匹配，会在升级当天把所有存量会话踢掉。
    /// </remarks>
    public static bool Matches(string? storedUserAgent, string? currentUserAgent)
    {
        var stored = Fingerprint(storedUserAgent);
        var current = Fingerprint(currentUserAgent);

        if (stored == null || current == null)
        {
            return true;
        }

        return string.Equals(stored, current, StringComparison.Ordinal);
    }

    /// <summary>
    /// 归一化：转小写、抹掉全部数字串、压缩空白。
    /// </summary>
    private static string Normalize(string userAgent)
    {
        var builder = new StringBuilder(userAgent.Length);
        var lastWasSpace = false;

        foreach (var ch in userAgent)
        {
            if (char.IsAsciiDigit(ch))
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }
}
