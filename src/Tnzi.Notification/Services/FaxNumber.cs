namespace Tnzi.Notification.Services;

/// <summary>
/// 传真号码的归一化：把人写的号码变成网关能收的那一串数字。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>这条规则属于框架，不属于调用方</b>，理由是它错了**没有症状**：带前导 1 的北美号码
/// 被网关照单全收，然后那份传真永远不会到达 —— 既不退信，也不报错，日志里是一次成功的投递。
/// 每个自己实现一遍的项目都会独立地重新踩一次，而且是在客户问"我没收到"的时候才发现。
/// </para>
/// <para>
/// 纯函数、无状态，故是 <c>static</c>。**公开**而不是 internal：消费应用在联系人表单上校验
/// 传真号时用的是同一条规则，藏起来只会让它们再写一遍。
/// </para>
/// <para>
/// ★ <b>已知边界：11 位且以 1 打头的号码一律按北美处理。</b>国家码 1 是北美独占的，所以
/// <b>国际形态</b>的号码不会误判；但少数国家的<b>国内形态</b>也是 11 位、也以 1 开头
/// （最典型的是中国大陆手机号），那种写法会被砍掉首位。传真号极少落在这个区间
/// （中国大陆固话的国内形态以 0 开头），且规避办法是确定的：<b>国际号码一律按完整国际形态存</b>
/// （<c>+86 10 …</c> = 12 位，不触发这条规则）。这里刻意不去猜"这到底是哪国号码" ——
/// 猜错的两个方向都无症状，而带前导 1 的北美号码是实际会天天发生的那一个。
/// </para>
/// </remarks>
public static class FaxNumber
{
    /// <summary>
    /// 号码里允许出现的非数字字符（纯排版用，会被丢弃）。
    /// </summary>
    /// <remarks>
    /// 白名单而不是"过滤掉所有非数字"：<c>9055551234 ext. 99</c> 这种在"只留数字"的做法下会变成
    /// <c>905555123499</c> —— 一个语法上完全合法、网关照收、然后石沉大海的号码。分机拨不进传真机，
    /// 所以这里宁可当场拒绝，让调用方自己决定怎么办。
    /// </remarks>
    private const string AllowedSeparators = "+-()./";

    /// <summary>能构成一个可拨号码的最少数字位数。</summary>
    /// <remarks>比这更短的只可能是录入残缺（分机号、区号片段）。取 7 是国际上仍在使用的最短完整号码长度。</remarks>
    public const int MinDigits = 7;

    /// <summary>能构成一个可拨号码的最多数字位数（E.164 上限）。</summary>
    public const int MaxDigits = 15;

    /// <summary>北美编号计划（NANP）的国内号码位数。</summary>
    private const int NanpDigits = 10;

    /// <summary>
    /// 归一化一个传真号码：只留数字；北美号码写成 11 位且以 1 打头时去掉那个 1；
    /// 国际号码按其数字原样通过。
    /// </summary>
    /// <param name="raw">人写的号码，可带 <c>+ - ( ) . /</c> 与空白。</param>
    /// <param name="normalized">归一化后的纯数字号码；失败时为空串。</param>
    /// <param name="error">失败原因（面向开发者，英文）；成功时为 <c>null</c>。</param>
    /// <returns>能否归一化。</returns>
    public static bool TryNormalize(string? raw, out string normalized, out string? error)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Fax number is required.";
            return false;
        }

        var digits = new StringBuilder(raw.Length);
        foreach (var character in raw)
        {
            if (char.IsAsciiDigit(character))
            {
                digits.Append(character);
                continue;
            }

            if (char.IsWhiteSpace(character) || AllowedSeparators.Contains(character, StringComparison.Ordinal))
                continue;

            error = $"Fax number '{raw}' contains '{character}', which is not a digit or one of the separators '+-().' and whitespace. " +
                    "Extensions and dialling codes cannot be sent to a fax machine - strip them before sending.";
            return false;
        }

        var value = digits.ToString();

        // ★ 唯一的实质规则：北美 11 位、以 1 打头 = 长途前缀，网关要的是不带前缀的 10 位。
        // 只在"正好 11 位"时成立 —— 10 位的 NANP 号码区号不会以 1 开头，而 12 位以上以 1 开头的
        // 是别国的号码，砍掉那一位会把它拨到另一个地方去。
        if (value.Length == NanpDigits + 1 && value[0] == '1')
        {
            value = value[1..];
        }

        if (value.Length < MinDigits)
        {
            error = $"Fax number '{raw}' has only {value.Length} digit(s); at least {MinDigits} are needed to dial.";
            return false;
        }

        if (value.Length > MaxDigits)
        {
            error = $"Fax number '{raw}' has {value.Length} digits; E.164 allows at most {MaxDigits}.";
            return false;
        }

        normalized = value;
        error = null;
        return true;
    }

    /// <summary>
    /// 归一化一个传真号码，失败时抛出。
    /// </summary>
    /// <remarks>号码已经确定合法（例如刚从库里读出来）时用这个；来自用户输入一律走
    /// <see cref="TryNormalize"/>，好把原因原样呈给用户。</remarks>
    /// <param name="raw">人写的号码。</param>
    /// <returns>归一化后的纯数字号码。</returns>
    /// <exception cref="ArgumentException"><paramref name="raw"/> 不是一个可拨号码。</exception>
    public static string Normalize(string raw)
    {
        if (TryNormalize(raw, out var normalized, out var error))
            return normalized;

        throw new ArgumentException(error, nameof(raw));
    }
}
