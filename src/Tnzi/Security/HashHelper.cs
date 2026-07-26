
namespace Tnzi.Security;

/// <summary>
/// 字符串Hash操作类
/// </summary>
public static class HashHelper
{
    /// <summary>
    /// 获取字符串的MD5哈希值, 默认编码为<see cref="Encoding.UTF8"/>
    /// </summary>
    public static string GetMd5(string value, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(value);

        encoding ??= Encoding.UTF8;
        byte[] bytes = encoding.GetBytes(value);
        return GetMd5(bytes);
    }

    /// <summary>
    /// 获取字节数组的MD5哈希值
    /// </summary>
    public static string GetMd5(byte[] bytes)
    {
        Check.NotNullOrEmpty(bytes);

        using var hash = MD5.Create();
        bytes = hash.ComputeHash(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取文件流的MD5哈希值
    /// </summary>
    public static string GetMd5(Stream stream)
    {
        Check.NotNull(stream);

        stream.Seek(0, SeekOrigin.Begin);
        using var hash = MD5.Create();
        byte[] hashBytes = hash.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取字符串的SHA1哈希值, 默认编码为<see cref="Encoding.UTF8"/>
    /// </summary>
    public static string GetSha1(string value, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(value);

        encoding ??= Encoding.UTF8;
        using var hash = SHA1.Create();
        byte[] bytes = hash.ComputeHash(encoding.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取字符串的SHA256哈希值, 默认编码为<see cref="Encoding.UTF8"/>
    /// </summary>
    public static string GetSha256(string value, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(value);

        encoding ??= Encoding.UTF8;
        using var hash = SHA256.Create();
        byte[] bytes = hash.ComputeHash(encoding.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取字符串的SHA512哈希值, 默认编码为<see cref="Encoding.UTF8"/>
    /// </summary>
    public static string GetSha512(string value, Encoding? encoding = null)
    {
        Check.NotNullOrEmpty(value);

        encoding ??= Encoding.UTF8;
        using var hash = SHA512.Create();
        byte[] bytes = hash.ComputeHash(encoding.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取字节数组的SHA256哈希值
    /// </summary>
    public static string GetSha256(byte[] bytes)
    {
        Check.NotNullOrEmpty(bytes);

        using var hash = SHA256.Create();
        byte[] hashBytes = hash.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取文件流的SHA256哈希值
    /// </summary>
    public static string GetSha256(Stream stream)
    {
        Check.NotNull(stream);

        stream.Seek(0, SeekOrigin.Begin);
        using var hash = SHA256.Create();
        byte[] hashBytes = hash.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 口令哈希默认迭代次数（PBKDF2-HMAC-SHA256）。
    /// </summary>
    /// <remarks>
    /// 随时间上调是预期行为：<see cref="VerifyPassword"/> 从**存储的**串里读迭代次数，
    /// 所以调高只影响新哈希，存量继续按各自当初的次数校验，无需重置任何人的口令。
    /// </remarks>
    public const int DefaultPasswordIterations = 210_000;

    private const int PasswordSaltBytes = 16;
    private const int PasswordHashBytes = 32;

    /// <summary>
    /// 为口令生成加盐哈希，格式 <c>pbkdf2$sha256${iterations}${saltBase64}${hashBase64}</c>。
    /// </summary>
    /// <remarks>
    /// 口令**不能**用 <see cref="GetSha256(string, Encoding?)"/> 之类的裸哈希存：
    /// 裸哈希无盐（同口令产生同摘要，可彩虹表反查）且计算太快（可离线爆破）。
    /// 这里用 PBKDF2-HMAC-SHA256 + 每条随机盐 + 高迭代次数。
    /// 迭代次数与盐**存在结果串里**，故日后调参不会让存量哈希失效。
    /// </remarks>
    /// <param name="password">明文口令</param>
    /// <param name="iterations">迭代次数，默认 <see cref="DefaultPasswordIterations"/></param>
    public static string HashPassword(string password, int iterations = DefaultPasswordIterations)
    {
        Check.NotNullOrEmpty(password);
        if (iterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be greater than zero.");

        var salt = RandomNumberGenerator.GetBytes(PasswordSaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, PasswordHashBytes);
        return $"pbkdf2$sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// 校验口令是否匹配 <see cref="HashPassword"/> 产出的串。
    /// </summary>
    /// <remarks>
    /// 比较走 <see cref="CryptographicOperations.FixedTimeEquals"/>：逐字节短路比较会把
    /// "前几位对不对"经耗时泄漏出去。格式不认识一律返回 false（不抛异常——校验失败与
    /// 数据损坏对调用方是同一个结论：不放行）。
    ///
    /// ★**空的盐或摘要必须当作损坏而不是"长度为零的摘要"**：派生长度取自存储的摘要，
    /// 若摘要段为空（列被截断、手工改库、写了一半的哈希），两侧都会是零长数组，而
    /// <c>FixedTimeEquals(空, 空)</c> 为真——任意口令都能通过。这条守卫必须在派生
    /// 之前。长度本身**不**钉死成常量：那样一旦调整 <see cref="PasswordHashBytes"/>
    /// 就会让全部存量哈希失效（比放宽长度严重得多）。
    /// </remarks>
    public static bool VerifyPassword(string password, string? storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            return false;

        var parts = storedHash.Split('$');
        if (parts.Length != 5 || parts[0] != "pbkdf2" || parts[1] != "sha256")
            return false;
        if (!int.TryParse(parts[2], out var iterations) || iterations <= 0)
            return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
