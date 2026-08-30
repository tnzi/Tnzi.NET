namespace Tnzi.Security;

/// <summary>
/// 一次性凭据原语：签发一枚高熵随机令牌，库里只留它的哈希。
/// </summary>
/// <remarks>
/// <para>
/// <b>适用的场景有一个共同形状</b>：持有令牌的那一端没有登录、没有账号，
/// <b>拿着这串字符本身就是全部凭据</b> —— 签署链接、注册邀请、领取码、找回入口都是这个形状。
/// 因此令牌的随机性与「库里不存明文」这两件事，就是整条链路的全部安全性。
/// </para>
/// <para>
/// <b>刻意不加盐、不做慢哈希</b>，与口令存储的取舍正相反：令牌是密码学随机数，
/// 没有字典可查、没有彩虹表可撞，慢哈希只会给每次校验徒增延迟；而按哈希做等值查询
/// 要求同一输入恒得同一输出，<b>加盐就查不了了</b>。
/// </para>
/// <para>
/// <b>本类型只管算法，不碰存储。</b>令牌存在哪张表、怎么标记已消费、过期怎么清理，
/// 各业务差别很大（有的挂在用户身上、有的挂在收件人身上），强行统一存储只会让两边都别扭。
/// 需要的存储语义一般是三样：<c>Hash</c> 上一条唯一索引、一个过期时间、一个已消费标记。
/// </para>
/// <para>
/// ★ 用哈希查不到时，<b>失效、过期、不存在应当回答同一句话</b>。区分开就是在帮人
/// 试探哪些令牌是真的。这一条属于调用方，本类型无从代劳。
/// </para>
/// </remarks>
public static class OneTimeToken
{
    /// <summary>
    /// 默认熵，单位字节。32 字节 = 256 位，编码后是 43 个字符。
    /// </summary>
    public const int DefaultEntropyBytes = 32;

    /// <summary>
    /// 令牌熵的下限，单位字节。低于它的令牌可被穷举，签发即无意义。
    /// </summary>
    public const int MinimumEntropyBytes = 16;

    /// <summary>
    /// 存储哈希的字符长度：SHA-256 的 32 字节 = 64 个十六进制字符。
    /// </summary>
    /// <remarks>建表时列宽按它取，不要凭记忆写 <c>128</c> 或 <c>256</c>。</remarks>
    public const int HashLength = 64;

    /// <summary>
    /// 签发一枚新令牌，返回 URL 安全的 base64（无填充）。
    /// </summary>
    /// <param name="entropyBytes">熵的字节数，默认 <see cref="DefaultEntropyBytes"/>。</param>
    /// <remarks>
    /// 结果不含 <c>+</c> <c>/</c> <c>=</c>：进 URL 时它们要么被转义、要么被中间设备改写，
    /// 而一条被改写过的链接是打不开的。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">熵低于 <see cref="MinimumEntropyBytes"/>。</exception>
    public static string Create(int entropyBytes = DefaultEntropyBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(entropyBytes, MinimumEntropyBytes);

        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(entropyBytes));
    }

    /// <summary>
    /// 计算令牌的存储哈希（SHA-256，小写十六进制）。
    /// </summary>
    /// <remarks>
    /// 空白输入一律拒绝：空串会哈希出一个完全合法的值，
    /// 于是「这条记录没有令牌」就变成了一条能被查到的记录。
    /// </remarks>
    /// <exception cref="ArgumentException">令牌为 null 或空白。</exception>
    public static string Hash(string token)
    {
        Check.NotNullOrWhiteSpace(token);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>
    /// 校验令牌是否对应给定的存储哈希。
    /// </summary>
    /// <param name="token">待校验的令牌明文；为空白时恒为 <c>false</c>。</param>
    /// <param name="storedHash">库里存着的哈希；为空白时恒为 <c>false</c>。</param>
    /// <remarks>
    /// <para>
    /// 走固定时间比较。按哈希查询的路径其实用不上它（数据库先比中的），
    /// 但「先取出记录、再比对哈希」这种写法同样常见，那条路上的计时差是真的。
    /// </para>
    /// <para>
    /// <b>这不能替代「令牌是否已消费 / 是否过期」的判断</b>，那两件事在存储层，调用方自己管。
    /// </para>
    /// </remarks>
    public static bool MatchesHash(string? token, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var computed = Encoding.UTF8.GetBytes(Hash(token));
        var expected = Encoding.UTF8.GetBytes(storedHash);

        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }
}
