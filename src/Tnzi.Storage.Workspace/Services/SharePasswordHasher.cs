namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 分享链接口令的哈希：PBKDF2-SHA256（慢哈希），并继续认得出 2026-09-04 之前写下的单轮 HMAC 格式。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么要慢哈希。</b>在线爆破被 <c>Share.MaxFailedPasswordAttempts</c>（默认 10 次即停用）挡住，
/// 所以单轮 HMAC 的影响面是<b>库被拖走之后</b>的离线爆破：分享口令通常是人随手起的短口令，
/// 单轮 HMAC-SHA256 每秒能试几十亿次，一张 GPU 一晚上就能把库里全部口令还原出来 ——
/// 而链接本身（令牌 + 口令）泄漏后这些口令又常常与别处复用。Identity 的口令走 PBKDF2，
/// 分享口令没有理由是更弱的那一档。迭代次数与 ASP.NET Core Identity 的 <c>PasswordHasher</c>
/// V3 默认值同源（100,000），两处的判断标准一致。
/// </para>
/// <para>
/// 存储格式带版本前缀：<c>pbkdf2$&lt;迭代次数&gt;$&lt;盐 hex&gt;$&lt;哈希 hex&gt;</c>。
/// 旧格式 <c>&lt;盐 hex&gt;:&lt;哈希 hex&gt;</c>（单轮 HMAC-SHA256）**仍然能验**，且在验证通过的那一刻
/// 由调用方就地升级 —— 库里存量链接不需要重发，也不会有一个「口令突然全错了」的升级窗口。
/// </para>
/// </remarks>
public static class SharePasswordHasher
{
    /// <summary>与 ASP.NET Core Identity PasswordHasher V3 的默认迭代次数一致。</summary>
    public const int Iterations = 100_000;

    private const string Prefix = "pbkdf2";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>算一枚新口令的哈希（当前格式）。</summary>
    public static string Hash(string password)
    {
        Check.NotNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, Iterations);
        return $"{Prefix}${Iterations}${Convert.ToHexString(salt)}${Convert.ToHexString(hash)}".ToLowerInvariant();
    }

    /// <summary>
    /// 校验口令。<paramref name="needsRehash"/> 为 true 表示存的是旧格式（或旧参数），
    /// 调用方应在通过后用 <see cref="Hash"/> 重算一枚写回去。
    /// </summary>
    public static bool Verify(string password, string storedHash, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            return false;

        if (storedHash.StartsWith(Prefix + "$", StringComparison.OrdinalIgnoreCase))
            return VerifyPbkdf2(password, storedHash, out needsRehash);

        // 旧格式：salt:hash，单轮 HMAC-SHA256。通过即标记升级。
        if (!VerifyLegacyHmac(password, storedHash))
            return false;

        needsRehash = true;
        return true;
    }

    private static bool VerifyPbkdf2(string password, string storedHash, out bool needsRehash)
    {
        needsRehash = false;
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromHexString(parts[2]);
            expected = Convert.FromHexString(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Derive(password, salt, iterations);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            return false;

        // 迭代次数低于当前标准的旧记录也升级（日后调高 Iterations 时自动生效）。
        needsRehash = iterations < Iterations;
        return true;
    }

    private static bool VerifyLegacyHmac(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2)
            return false;

        try
        {
            var salt = Convert.FromHexString(parts[0]);
            var expected = Convert.FromHexString(parts[1]);
            using var hmac = new HMACSHA256(salt);
            var actual = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
