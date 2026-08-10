using System.Security.Cryptography;

namespace Tnzi.Signing.Services;

/// <summary>
/// 一次性签署链接的令牌原语。
/// </summary>
/// <remarks>
/// <para>
/// <b>明文只出现在邮件里的那条 URL 中，库里只存 <see cref="Hash"/> 的结果。</b>
/// 一份泄漏的数据库备份不该等同于一叠可用的签署链接。查找也走哈希比对，
/// 而不是拿一个秘密去做等值查询。
/// </para>
/// <para>
/// 令牌是 256 位密码学随机数，不是可猜的序列 —— 它本身就是全部凭据，
/// 因为链接那一端没有登录、没有账号，只有"持有这条链接的人"。
/// </para>
/// </remarks>
public static class SigningToken
{
    /// <summary>令牌熵（字节）。32 字节 = 256 位。</summary>
    private const int TokenBytes = 32;

    /// <summary>签发一个新的一次性令牌（URL 安全的 base64，无填充）。</summary>
    public static string Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// 计算令牌的存储哈希（SHA-256，小写十六进制）。
    /// </summary>
    /// <remarks>
    /// 这里<b>不加盐、不做慢哈希</b>，与口令存储的取舍刻意不同：令牌是 256 位的密码学随机数，
    /// 没有字典可查、没有彩虹表可撞，慢哈希只会给每次链接打开徒增延迟；而按哈希做等值查询
    /// 要求同一输入恒得同一输出，加盐就查不了了。
    /// </remarks>
    public static string Hash(string token)
    {
        Check.NotNullOrWhiteSpace(token);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }
}
